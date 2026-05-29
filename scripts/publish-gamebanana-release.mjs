#!/usr/bin/env node
import { appendFile } from "node:fs/promises";
import { readFile, stat } from "node:fs/promises";
import { basename } from "node:path";
import { chromium } from "playwright";

const gamebananaOrigin = "https://gamebanana.com";

function requiredEnv(name) {
  const value = process.env[name];
  if (!value || value.startsWith("TODO_")) {
    throw new Error(`${name} is required`);
  }
  return value;
}

function optionalEnv(name, fallback) {
  const value = process.env[name];
  return value && value.length > 0 ? value : fallback;
}

function escapeHtml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function markdownToGameBananaHtml(markdown) {
  const html = [];
  let listOpen = false;

  for (const rawLine of markdown.split(/\r?\n/)) {
    const line = rawLine.trim();

    if (!line) {
      if (listOpen) {
        html.push("</ul>");
        listOpen = false;
      }
      continue;
    }

    const heading = line.match(/^#{2,6}\s+(.*)$/);
    if (heading) {
      if (listOpen) {
        html.push("</ul>");
        listOpen = false;
      }
      html.push(`<h3>${escapeHtml(heading[1])}</h3>`);
      continue;
    }

    const bullet = line.match(/^[-*]\s+(.*)$/);
    if (bullet) {
      if (!listOpen) {
        html.push("<ul>");
        listOpen = true;
      }
      html.push(`<li>${escapeHtml(bullet[1])}</li>`);
      continue;
    }

    if (listOpen) {
      html.push("</ul>");
      listOpen = false;
    }
    html.push(`<p>${escapeHtml(line)}</p>`);
  }

  if (listOpen) {
    html.push("</ul>");
  }

  return html.join("\n");
}

function categoryForHeading(heading) {
  const normalized = heading.toLowerCase();
  if (normalized.includes("fix")) return "BugFix";
  if (normalized.includes("remove")) return "Removal";
  if (normalized.includes("add")) return "Addition";
  if (normalized.includes("change") || normalized.includes("improve")) {
    return "Improvement";
  }
  return "Adjustment";
}

function parseChangeLog(markdown) {
  const entries = [];
  let currentCategory = "Adjustment";

  for (const rawLine of markdown.split(/\r?\n/)) {
    const line = rawLine.trim();
    const heading = line.match(/^#{2,6}\s+(.*)$/);
    if (heading) {
      currentCategory = categoryForHeading(heading[1]);
      continue;
    }

    const bullet = line.match(/^[-*]\s+(.*)$/);
    if (bullet) {
      entries.push({
        text: bullet[1].replace(/\s+/g, " ").trim(),
        cat: currentCategory,
      });
    }
  }

  if (entries.length > 0) {
    return entries;
  }

  return [{ text: `Published ${process.env.RELEASE_TAG}.`, cat: "Addition" }];
}

function collectFileRowIds(value, ids = new Set()) {
  if (Array.isArray(value)) {
    for (const item of value) {
      collectFileRowIds(item, ids);
    }
    return ids;
  }

  if (!value || typeof value !== "object") {
    return ids;
  }

  for (const [key, nestedValue] of Object.entries(value)) {
    if (/(?:row|file).*id|id.*(?:row|file)|rowid|fileid/i.test(key)) {
      if (typeof nestedValue === "number") {
        ids.add(nestedValue);
      }

      if (typeof nestedValue === "string" && /^\d+$/.test(nestedValue)) {
        ids.add(Number(nestedValue));
      }
    }

    collectFileRowIds(nestedValue, ids);
  }

  return ids;
}

async function fetchJson(request, url, options = {}) {
  const response = await request.fetch(url, {
    ...options,
    headers: {
      accept: "application/json",
      ...(options.headers ?? {}),
    },
  });

  const text = await response.text();
  if (!response.ok()) {
    throw new Error(
      `${options.method ?? "GET"} ${url} failed with ${response.status()}: ${text}`,
    );
  }

  let payload;
  try {
    payload = text ? JSON.parse(text) : {};
  } catch {
    throw new Error(`${url} returned non-JSON response: ${text.slice(0, 500)}`);
  }

  if (payload && typeof payload === "object" && "_sErrorCode" in payload) {
    throw new Error(`${url} returned GameBanana error: ${JSON.stringify(payload)}`);
  }

  return payload;
}

async function getFileRowIds(request, apiSection, submissionId) {
  const files = await fetchJson(
    request,
    `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Files`,
  );
  return collectFileRowIds(files);
}

async function logIn(page, username, password) {
  try {
    await fetchJson(page.context().request, `${gamebananaOrigin}/apiv11/Member/Authenticate`, {
      method: "POST",
      data: {
        _sUsername: username,
        _sPassword: password,
      },
    });
  } catch (error) {
    if (error instanceof Error && error.message.includes("UNKNOWN_DEVICE")) {
      throw new Error(
        "GameBanana requires captcha verification for this runner. Set GAMEBANANA_STORAGE_STATE_B64 from a manually authenticated Playwright storage state.",
      );
    }
    throw error;
  }

  await page.goto(`${gamebananaOrigin}/members/account/login`, {
    waitUntil: "domcontentloaded",
  });

  const alreadyLoggedIn = await page
    .getByText(username, { exact: false })
    .first()
    .isVisible()
    .catch(() => false);
  if (alreadyLoggedIn) {
    return;
  }

  const passwordInput = page.getByRole("textbox", { name: /^password$/i }).first();
  await passwordInput.waitFor({ state: "visible", timeout: 20_000 });

  const usernameInput = page.getByRole("textbox", { name: /^username$/i }).first();
  await usernameInput.waitFor({ state: "visible", timeout: 20_000 });

  await usernameInput.fill(username);
  await passwordInput.fill(password);

  await Promise.all([
    page.waitForLoadState("networkidle").catch(() => undefined),
    page.getByRole("button", { name: /^login$/i }).first().click(),
  ]);

  const stillOnLogin = await page
    .locator('input[type="password"]')
    .first()
    .isVisible()
    .catch(() => false);
  if (stillOnLogin) {
    throw new Error("GameBanana login did not complete; check GAMEBANANA_USERNAME/PASSWORD.");
  }
}

async function uploadReleaseAsset(page, pageSection, submissionId, assetPath) {
  await page.goto(`${gamebananaOrigin}/${pageSection}/edit/${submissionId}`, {
    waitUntil: "domcontentloaded",
  });

  const permissionMessages = page.locator("#EditFormModule .LogMessages").first();
  if (await permissionMessages.isVisible().catch(() => false)) {
    const message = await permissionMessages.innerText();
    const error = new Error(`GameBanana edit form is not available: ${message}`);
    error.code = "GAMEBANANA_EDIT_PERMISSION_DENIED";
    throw error;
  }

  const mediaTab = page.getByText(/^media$/i).first();
  if (await mediaTab.isVisible().catch(() => false)) {
    await mediaTab.click();
  }

  const fileInput = page.locator('input[type="file"]').first();
  await fileInput.waitFor({ state: "attached", timeout: 20_000 });
  await fileInput.setInputFiles(assetPath);

  await page.waitForTimeout(2_000);

  await Promise.all([
    page.waitForLoadState("networkidle").catch(() => undefined),
    page.locator('button[type="submit"], input[type="submit"]').last().click(),
  ]);
}

function redactKnownSecrets(value) {
  let redacted = value;
  for (const name of ["GAMEBANANA_USERNAME", "GAMEBANANA_PASSWORD"]) {
    const secret = process.env[name];
    if (secret) {
      redacted = redacted.replaceAll(secret, `[${name}]`);
    }
  }
  return redacted;
}

async function logPageState(page, label) {
  const [title, hasEditForm, hasFileInput, hasPasswordInput, bodyText] = await Promise.all([
    page.title().catch(() => ""),
    page.locator("#EditFormModule").first().isVisible().catch(() => false),
    page.locator('input[type="file"]').first().isVisible().catch(() => false),
    page.locator('input[type="password"]').first().isVisible().catch(() => false),
    page.locator("body").innerText({ timeout: 2_000 }).catch(() => ""),
  ]);

  const excerpt = redactKnownSecrets(bodyText.replace(/\s+/g, " ").trim()).slice(0, 500);
  console.log(
    `${label}: url=${page.url()} title=${JSON.stringify(title)} editForm=${hasEditForm} fileInput=${hasFileInput} passwordInput=${hasPasswordInput}`,
  );
  console.log(`${label} body excerpt: ${excerpt}`);
}

async function setGithubOutput(name, value) {
  const outputPath = process.env.GITHUB_OUTPUT;
  if (!outputPath) {
    return;
  }

  await appendFile(outputPath, `${name}=${value}\n`, "utf8");
}

async function main() {
  const storageState = optionalEnv("GAMEBANANA_STORAGE_STATE", "");
  const username = storageState ? optionalEnv("GAMEBANANA_USERNAME", "") : requiredEnv("GAMEBANANA_USERNAME");
  const password = storageState ? optionalEnv("GAMEBANANA_PASSWORD", "") : requiredEnv("GAMEBANANA_PASSWORD");
  const submissionId = requiredEnv("GAMEBANANA_SUBMISSION_ID");
  const apiSection = optionalEnv("GAMEBANANA_API_SECTION", "Mod");
  const pageSection = optionalEnv("GAMEBANANA_PAGE_SECTION", "mods");
  const releaseTag = requiredEnv("RELEASE_TAG");
  const releaseAsset = requiredEnv("RELEASE_ASSET");
  const releaseNotesFile = requiredEnv("RELEASE_NOTES_FILE");
  const releaseName = optionalEnv("RELEASE_NAME", `Akron ${releaseTag}`);

  await stat(releaseAsset);
  const releaseNotes = await readFile(releaseNotesFile, "utf8");
  const version = releaseTag.replace(/^v/, "");

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext(
    storageState
      ? {
          storageState,
        }
      : {},
  );
  const page = await context.newPage();

  try {
    if (!storageState) {
      await logIn(page, username, password);
    }

    const beforeIds = await getFileRowIds(context.request, apiSection, submissionId);
    try {
      await uploadReleaseAsset(page, pageSection, submissionId, releaseAsset);
    } catch (error) {
      if (
        storageState &&
        username &&
        password &&
        error instanceof Error &&
        error.code === "GAMEBANANA_EDIT_PERMISSION_DENIED"
      ) {
        await logPageState(page, "Stored GameBanana session edit denial");
        console.log("Stored GameBanana session could not edit the submission; retrying with credentials.");
        await logIn(page, username, password);
        await uploadReleaseAsset(page, pageSection, submissionId, releaseAsset);
      } else {
        throw error;
      }
    }

    let uploadedFileId = null;
    for (let attempt = 1; attempt <= 12; attempt += 1) {
      const afterIds = await getFileRowIds(context.request, apiSection, submissionId);
      const newIds = [...afterIds].filter((id) => !beforeIds.has(id));
      if (newIds.length > 0) {
        uploadedFileId = Math.max(...newIds);
        break;
      }

      if (afterIds.size > 0) {
        uploadedFileId = Math.max(...afterIds);
      }

      await page.waitForTimeout(5_000);
    }

    if (!uploadedFileId) {
      throw new Error(`Could not determine GameBanana file row ID for ${basename(releaseAsset)}.`);
    }

    await fetchJson(context.request, `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Update`, {
      method: "POST",
      data: {
        _aChangeLog: parseChangeLog(releaseNotes),
        _aFileRowIds: [uploadedFileId],
        _sName: releaseName,
        _sVersion: version,
        _sText: markdownToGameBananaHtml(releaseNotes),
      },
    });

    await setGithubOutput("file_id", uploadedFileId);
    console.log(`Published ${releaseTag} to GameBanana submission ${submissionId}.`);
    console.log(`GameBanana file ID: ${uploadedFileId}`);
  } finally {
    await browser.close();
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
