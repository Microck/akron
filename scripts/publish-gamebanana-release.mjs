#!/usr/bin/env node
import { appendFile, mkdir, writeFile } from "node:fs/promises";
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

function sameSiteValue(value) {
  const normalized = String(value ?? "").toLowerCase();
  if (normalized === "strict") return "Strict";
  if (normalized === "none" || normalized === "no_restriction") return "None";
  return "Lax";
}

function booleanCookieValue(value, fallback) {
  if (value === undefined || value === null) return fallback;
  if (typeof value === "boolean") return value;
  return String(value).toLowerCase() === "true";
}

function normalizeCookie(rawCookie) {
  if (!rawCookie || typeof rawCookie !== "object") {
    throw new Error("GAMEBANANA_COOKIES contains a non-object cookie entry.");
  }

  const name = rawCookie.name;
  const value = rawCookie.value;
  if (typeof name !== "string" || typeof value !== "string") {
    throw new Error("GAMEBANANA_COOKIES cookie entries must include string name and value fields.");
  }

  const domain = rawCookie.domain ?? rawCookie.host ?? ".gamebanana.com";
  const path = rawCookie.path ?? "/";
  const expires = rawCookie.expires ?? rawCookie.expirationDate ?? -1;

  return {
    name,
    value,
    domain: String(domain).replace(/^https?:\/\//, ""),
    path: String(path),
    expires: Number.isFinite(Number(expires)) ? Number(expires) : -1,
    httpOnly: booleanCookieValue(rawCookie.httpOnly ?? rawCookie.http_only, false),
    secure: booleanCookieValue(rawCookie.secure, true),
    sameSite: sameSiteValue(rawCookie.sameSite ?? rawCookie.same_site),
  };
}

function cookiesFromPayload(payload) {
  if (Array.isArray(payload)) {
    return payload.map(normalizeCookie);
  }

  if (!payload || typeof payload !== "object") {
    throw new Error("GAMEBANANA_COOKIES must decode to JSON object or array.");
  }

  if (Array.isArray(payload.cookies)) {
    return payload.cookies.map(normalizeCookie);
  }

  return Object.entries(payload).map(([name, value]) =>
    normalizeCookie({
      name,
      value: String(value),
      domain: ".gamebanana.com",
    }),
  );
}

function decodeCookieBundle(value) {
  if (!value) {
    return [];
  }

  const decoded = Buffer.from(value, "base64").toString("utf8");
  const payload = JSON.parse(decoded);
  return cookiesFromPayload(payload).filter((cookie) =>
    cookie.domain.replace(/^\./, "").endsWith("gamebanana.com"),
  );
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

function getUpdateRecords(value) {
  if (Array.isArray(value)) {
    return value;
  }

  if (value && typeof value === "object" && Array.isArray(value._aRecords)) {
    return value._aRecords;
  }

  return [];
}

function findReleaseUpdate(updates, releaseName, version) {
  return getUpdateRecords(updates)
    .filter((update) => update && typeof update === "object")
    .filter((update) => update._sVersion === version || update._sName === releaseName)
    .sort((left, right) => Number(right._tsDateAdded ?? 0) - Number(left._tsDateAdded ?? 0))[0];
}

function updateFileRowIds(update) {
  const ids = new Set();

  if (Array.isArray(update?._aFileRowIds)) {
    for (const id of update._aFileRowIds) {
      if (typeof id === "number") {
        ids.add(id);
      }

      if (typeof id === "string" && /^\d+$/.test(id)) {
        ids.add(Number(id));
      }
    }
  }

  collectFileRowIds(update?._aFiles, ids);
  return ids;
}

function normalizedVersion(value) {
  return String(value ?? "").replace(/^v/i, "");
}

function findFileById(files, fileId) {
  return getUpdateRecords(files).find((file) => Number(file?._idRow) === Number(fileId));
}

function findReleaseFile(files, version) {
  return getUpdateRecords(files)
    .filter((file) => releaseFileMatches(file, version))
    .filter((file) => file._bIsArchived !== true)
    .sort((left, right) => Number(right._tsDateAdded ?? 0) - Number(left._tsDateAdded ?? 0))[0];
}

function releaseFileMatches(file, version) {
  if (!file || typeof file !== "object") {
    return false;
  }

  if (file._sVersion && normalizedVersion(file._sVersion) === normalizedVersion(version)) {
    return true;
  }

  const normalizedFileName = String(file._sFile ?? "").toLowerCase().replace(/[^a-z0-9]+/g, "");
  const normalizedRelease = normalizedVersion(version).toLowerCase().replace(/[^a-z0-9]+/g, "");
  return normalizedRelease.length > 0 && normalizedFileName.includes(normalizedRelease);
}

async function publishReleaseUpdate(request, apiSection, submissionId, releaseDetails, fileId) {
  await fetchJson(request, `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Update`, {
    method: "POST",
    data: {
      _aChangeLog: parseChangeLog(releaseDetails.notes),
      _aFileRowIds: [fileId],
      _sName: releaseDetails.name,
      _sVersion: releaseDetails.version,
      _sText: markdownToGameBananaHtml(releaseDetails.notes),
    },
  });
}

async function trashUpdate(request, updateId) {
  await fetchJson(request, `${gamebananaOrigin}/apiv12/Update/${updateId}`, {
    method: "DELETE",
    data: {
      _idReasonRow: "2",
      _sNotes: "Replacing an automated release update that was linked to the wrong file.",
    },
  });
}

async function waitForLinkedReleaseUpdate(
  page,
  request,
  apiSection,
  submissionId,
  releaseDetails,
  fileId,
) {
  let releaseUpdate = null;
  for (let attempt = 1; attempt <= 12; attempt += 1) {
    const updates = await fetchJson(
      request,
      `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Updates`,
    );
    releaseUpdate = findReleaseUpdate(updates, releaseDetails.name, releaseDetails.version);
    if (releaseUpdate && updateFileRowIds(releaseUpdate).has(fileId)) {
      return releaseUpdate;
    }

    await page.waitForTimeout(5_000);
  }

  if (!releaseUpdate) {
    throw new Error(
      `GameBanana did not expose an update named "${releaseDetails.name}" or version "${releaseDetails.version}" after publishing.`,
    );
  }

  const linkedFileIds = [...updateFileRowIds(releaseUpdate)].sort((left, right) => left - right);
  throw new Error(
    `GameBanana update ${releaseUpdate._idRow ?? "(unknown id)"} is linked to file IDs ${linkedFileIds.join(", ") || "(none)"} instead of uploaded file ${fileId}. Refusing to sync public links to a file that is not attached to the release update.`,
  );
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

  const fileInput = page.locator('#Files input[type="file"]').first();
  await fileInput.waitFor({ state: "attached", timeout: 20_000 });
  const uploadedFileCount = await page.locator('#Files [id$="_UploadedFiles"] li').count();
  await fileInput.setInputFiles(assetPath);

  await page.waitForFunction(
    (previousCount) =>
      document.querySelectorAll('#Files [id$="_UploadedFiles"] li').length > previousCount &&
      document.querySelector("#Files .UploadMessage")?.textContent?.includes("Upload complete"),
    uploadedFileCount,
    { timeout: 120_000 },
  );

  const uploadForm = page.locator("form", { has: fileInput }).first();
  const submitButton = uploadForm.locator('button[type="submit"], input[type="submit"]').last();
  await submitButton.waitFor({ state: "visible", timeout: 20_000 });

  await new Promise((resolve) => setTimeout(resolve, 2_000));

  await Promise.all([
    page.waitForLoadState("networkidle").catch(() => undefined),
    submitButton.click(),
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

async function captureDebugArtifact(page, label) {
  const debugDir = process.env.GAMEBANANA_DEBUG_DIR;
  if (!debugDir) {
    return;
  }

  await mkdir(debugDir, { recursive: true });
  const slug = label.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  const [title, html, forms] = await Promise.all([
    page.title().catch(() => ""),
    page.content().catch(() => ""),
    page.locator("form").evaluateAll((formElements) =>
      formElements.map((form, formIndex) => ({
        formIndex,
        action: form.getAttribute("action"),
        method: form.getAttribute("method"),
        fileInputs: Array.from(form.querySelectorAll('input[type="file"]')).map((input) => ({
          name: input.getAttribute("name"),
          id: input.getAttribute("id"),
          files: Array.from(input.files ?? []).map((file) => file.name),
          visible:
            input instanceof HTMLElement &&
            input.offsetParent !== null &&
            getComputedStyle(input).visibility !== "hidden",
        })),
        submitControls: Array.from(
          form.querySelectorAll('button[type="submit"], input[type="submit"]'),
        ).map((control) => ({
          tag: control.tagName.toLowerCase(),
          type: control.getAttribute("type"),
          name: control.getAttribute("name"),
          id: control.getAttribute("id"),
          value: control.getAttribute("value"),
          text: control.textContent?.trim() ?? "",
          visible:
            control instanceof HTMLElement &&
            control.offsetParent !== null &&
            getComputedStyle(control).visibility !== "hidden",
        })),
      })),
    ).catch((error) => [{ error: error instanceof Error ? error.message : String(error) }]),
  ]);

  await writeFile(
    `${debugDir}/${slug}-summary.json`,
    JSON.stringify({ label, url: page.url(), title, forms }, null, 2),
    "utf8",
  );
  await writeFile(`${debugDir}/${slug}.html`, html, "utf8");
  await page.screenshot({ path: `${debugDir}/${slug}.png`, fullPage: true }).catch(() => undefined);
}

async function setGithubOutput(name, value) {
  const outputPath = process.env.GITHUB_OUTPUT;
  if (!outputPath) {
    return;
  }

  await appendFile(outputPath, `${name}=${value}\n`, "utf8");
}

async function createBrowserContext(browserName, storageState) {
  const contextOptions = storageState
    ? {
        storageState,
      }
    : {};

  if (browserName === "cloakbrowser") {
    const { launchContext } = await import("cloakbrowser");
    const context = await launchContext({
      headless: true,
      humanize: true,
      contextOptions,
    });
    console.log("Using CloakBrowser for GameBanana publishing.");
    return {
      context,
      close: () => context.close(),
    };
  }

  if (browserName !== "chromium") {
    throw new Error(`Unsupported GAMEBANANA_BROWSER value: ${browserName}`);
  }

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext(contextOptions);
  console.log("Using Playwright Chromium for GameBanana publishing.");
  return {
    context,
    close: () => browser.close(),
  };
}

async function main() {
  const browserName = optionalEnv("GAMEBANANA_BROWSER", "cloakbrowser");
  const storageState = optionalEnv("GAMEBANANA_STORAGE_STATE", "");
  const cookieBundle = optionalEnv("GAMEBANANA_COOKIES", "");
  const hasStoredAuth = Boolean(storageState || cookieBundle);
  const username = hasStoredAuth ? optionalEnv("GAMEBANANA_USERNAME", "") : requiredEnv("GAMEBANANA_USERNAME");
  const password = hasStoredAuth ? optionalEnv("GAMEBANANA_PASSWORD", "") : requiredEnv("GAMEBANANA_PASSWORD");
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
  const releaseDetails = {
    name: releaseName,
    notes: releaseNotes,
    version,
  };

  const browserSession = await createBrowserContext(browserName, storageState);
  const { context } = browserSession;
  const cookies = storageState ? [] : decodeCookieBundle(cookieBundle);
  if (storageState && cookieBundle) {
    console.log(
      "Using GAMEBANANA_STORAGE_STATE; ignoring GAMEBANANA_COOKIES to avoid overriding stored browser auth.",
    );
  }
  if (cookies.length > 0) {
    await context.addCookies(cookies);
    console.log(`Loaded ${cookies.length} GameBanana cookies from GAMEBANANA_COOKIES.`);
  }
  const page = await context.newPage();

  try {
    if (!hasStoredAuth) {
      await logIn(page, username, password);
    }

    const existingUpdates = await fetchJson(
      context.request,
      `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Updates`,
    );
    const existingReleaseUpdate = findReleaseUpdate(existingUpdates, releaseName, version);
    if (existingReleaseUpdate) {
      const files = await fetchJson(
        context.request,
        `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Files`,
      );
      const linkedFileIds = [...updateFileRowIds(existingReleaseUpdate)];
      const matchingLinkedFileId = linkedFileIds.find((fileId) =>
        releaseFileMatches(findFileById(files, fileId), version),
      );

      if (!matchingLinkedFileId) {
        const replacementFile = findReleaseFile(files, version);
        if (!replacementFile) {
          throw new Error(
            `GameBanana already has update ${existingReleaseUpdate._idRow ?? "(unknown id)"} for ${releaseTag}, but it is linked to file IDs ${linkedFileIds.join(", ") || "(none)"} and no uploaded ${version} file exists. Fix or delete that Update before rerunning the release.`,
          );
        }

        const replacementFileId = Number(replacementFile._idRow);
        await trashUpdate(context.request, existingReleaseUpdate._idRow);
        await publishReleaseUpdate(
          context.request,
          apiSection,
          submissionId,
          releaseDetails,
          replacementFileId,
        );
        await waitForLinkedReleaseUpdate(
          page,
          context.request,
          apiSection,
          submissionId,
          releaseDetails,
          replacementFileId,
        );
        await setGithubOutput("file_id", replacementFileId);
        console.log(
          `Replaced GameBanana update ${existingReleaseUpdate._idRow ?? "(unknown id)"} for ${releaseTag}.`,
        );
        console.log(`GameBanana file ID: ${replacementFileId}`);
        return;
      }

      await setGithubOutput("file_id", matchingLinkedFileId);
      console.log(
        `GameBanana update ${existingReleaseUpdate._idRow ?? "(unknown id)"} already exists for ${releaseTag}.`,
      );
      console.log(`GameBanana file ID: ${matchingLinkedFileId}`);
      return;
    }

    const beforeIds = await getFileRowIds(context.request, apiSection, submissionId);
    try {
      await uploadReleaseAsset(page, pageSection, submissionId, releaseAsset);
    } catch (error) {
      if (
        hasStoredAuth &&
        username &&
        password &&
        error instanceof Error &&
        error.code === "GAMEBANANA_EDIT_PERMISSION_DENIED"
      ) {
        await logPageState(page, "Stored GameBanana session edit denial");
        await captureDebugArtifact(page, "stored-gamebanana-session-edit-denial");
        console.log("Stored GameBanana session could not edit the submission; retrying with credentials.");
        await logIn(page, username, password);
        await uploadReleaseAsset(page, pageSection, submissionId, releaseAsset);
      } else {
        await logPageState(page, "GameBanana upload failed");
        await captureDebugArtifact(page, "gamebanana-upload-failed");
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

      await page.waitForTimeout(5_000);
    }

    if (!uploadedFileId) {
      await logPageState(page, "GameBanana upload did not create a new file");
      await captureDebugArtifact(page, "gamebanana-upload-did-not-create-a-new-file");
      throw new Error(`Could not determine GameBanana file row ID for ${basename(releaseAsset)}.`);
    }

    await publishReleaseUpdate(
      context.request,
      apiSection,
      submissionId,
      releaseDetails,
      uploadedFileId,
    );
    await waitForLinkedReleaseUpdate(
      page,
      context.request,
      apiSection,
      submissionId,
      releaseDetails,
      uploadedFileId,
    );

    await setGithubOutput("file_id", uploadedFileId);
    console.log(`Published ${releaseTag} to GameBanana submission ${submissionId}.`);
    console.log(`GameBanana file ID: ${uploadedFileId}`);
  } finally {
    await browserSession.close();
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
