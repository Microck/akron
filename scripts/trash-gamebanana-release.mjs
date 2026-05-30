import { chromium } from "playwright";
import { gunzipSync } from "node:zlib";

const gamebananaOrigin = "https://gamebanana.com";

function requiredEnv(name) {
  const value = process.env[name];
  if (!value) {
    throw new Error(`${name} is required.`);
  }
  return value;
}

function optionalEnv(name) {
  return process.env[name] ?? "";
}

function decodeJsonBase64(value, gzip = false) {
  if (!value) {
    return null;
  }

  const raw = Buffer.from(value, "base64");
  const text = gzip ? gunzipSync(raw).toString("utf8") : raw.toString("utf8");
  return JSON.parse(text);
}

function normalizeCookie(cookie) {
  const normalized = { ...cookie };
  if (!normalized.domain) {
    normalized.domain = ".gamebanana.com";
  }
  if (!normalized.path) {
    normalized.path = "/";
  }
  return normalized;
}

function decodeCookieBundle(value) {
  const decoded = decodeJsonBase64(value);
  if (!decoded) {
    return [];
  }

  const cookies = Array.isArray(decoded) ? decoded : decoded.cookies;
  if (!Array.isArray(cookies)) {
    throw new Error("GAMEBANANA_COOKIES must decode to a cookie array or storage-state object.");
  }

  return cookies.map(normalizeCookie);
}

async function fetchJson(context, url, options = {}) {
  const response = await context.fetch(url, {
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

  const payload = text ? JSON.parse(text) : {};
  if (payload && typeof payload === "object" && "_sErrorCode" in payload) {
    throw new Error(`${url} returned GameBanana error: ${JSON.stringify(payload)}`);
  }

  return payload;
}

function updateRecords(payload) {
  if (Array.isArray(payload)) {
    return payload;
  }
  if (Array.isArray(payload?._aRecords)) {
    return payload._aRecords;
  }
  return [];
}

function getFileRecords(payload) {
  if (Array.isArray(payload)) {
    return payload;
  }

  if (payload && typeof payload === "object") {
    return Object.values(payload);
  }

  return [];
}

async function trashResource(context, section, id, notes) {
  await fetchJson(context, `${gamebananaOrigin}/apiv12/${section}/${id}`, {
    method: "DELETE",
    data: {
      _idReasonRow: "2",
      _sNotes: notes,
    },
  });
}

async function getUpdate(context, apiSection, submissionId, updateId) {
  const updates = await fetchJson(
    context,
    `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Updates`,
  );
  return updateRecords(updates).find((record) => Number(record?._idRow) === Number(updateId));
}

async function trashUpdateIfPresent(context, apiSection, submissionId, updateId, notes) {
  const update = await getUpdate(context, apiSection, submissionId, updateId);
  if (!update) {
    console.log(`GameBanana update ${updateId} is already absent from the public update list.`);
    return;
  }

  if (update._bIsTrashed === true) {
    console.log(`GameBanana update ${updateId} is already trashed.`);
    return;
  }

  await trashResource(context, "Update", updateId, notes);
  console.log(`Trashed GameBanana update ${updateId}.`);
}

async function inspectFilesSection(page, pageSection, submissionId, fileId) {
  await page.goto(`${gamebananaOrigin}/${pageSection}/edit/${submissionId}`, {
    waitUntil: "domcontentloaded",
  });

  const mediaTab = page.getByText(/^media$/i).first();
  if (await mediaTab.isVisible().catch(() => false)) {
    await mediaTab.click();
  }

  const details = await page.locator("#Files").evaluate((section, targetFileId) => {
    const summarizeElement = (element) => ({
      tag: element.tagName.toLowerCase(),
      text: element.textContent?.replace(/\s+/g, " ").trim().slice(0, 160) ?? "",
      href: element instanceof HTMLAnchorElement ? element.href : null,
      type: element instanceof HTMLInputElement ? element.type : null,
      name: element instanceof HTMLInputElement ? element.name : null,
      id: element.id || null,
      value:
        element instanceof HTMLInputElement || element instanceof HTMLButtonElement
          ? element.value || null
          : null,
    });

    const matchingText = Array.from(section.querySelectorAll("li, tr, div"))
      .filter((element) => element.textContent?.includes(String(targetFileId)))
      .map(summarizeElement);

    return {
      text: section.textContent?.replace(/\s+/g, " ").trim().slice(0, 1200) ?? "",
      matchingText,
      links: Array.from(section.querySelectorAll("a")).map(summarizeElement),
      buttons: Array.from(section.querySelectorAll("button, input[type='button'], input[type='submit']")).map(
        summarizeElement,
      ),
      checkboxes: Array.from(section.querySelectorAll("input[type='checkbox']")).map(summarizeElement),
    };
  }, fileId);

  console.log(`GameBanana edit file section for ${fileId}: ${JSON.stringify(details, null, 2)}`);
}

async function getFile(context, apiSection, submissionId, fileId) {
  const files = await fetchJson(
    context,
    `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Files`,
  );
  return getFileRecords(files).find((record) => Number(record?._idRow) === Number(fileId));
}

async function archiveFileThroughEditForm(page, pageSection, submissionId, fileName) {
  await page.goto(`${gamebananaOrigin}/${pageSection}/edit/${submissionId}`, {
    waitUntil: "domcontentloaded",
  });

  const mediaTab = page.getByText(/^media$/i).first();
  if (await mediaTab.isVisible().catch(() => false)) {
    await mediaTab.click();
  }

  const archived = await page.locator("#Files").evaluate((section, targetFileName) => {
    const link = Array.from(section.querySelectorAll("a")).find(
      (anchor) => anchor.textContent?.trim() === targetFileName,
    );
    if (!link) {
      return false;
    }

    let element = link;
    while (element && element !== section) {
      const checkbox = element.querySelector('input[type="checkbox"][name="_bIsArchived"]');
      if (checkbox instanceof HTMLInputElement) {
        if (!checkbox.checked) {
          checkbox.checked = true;
          checkbox.dispatchEvent(new Event("input", { bubbles: true }));
          checkbox.dispatchEvent(new Event("change", { bubbles: true }));
        }
        return true;
      }
      element = element.parentElement;
    }

    return false;
  }, fileName);

  if (!archived) {
    throw new Error(`Could not find an archive checkbox for GameBanana file ${fileName}.`);
  }

  const filesSection = page.locator("#Files");
  const editForm = page.locator("form", { has: filesSection }).first();
  const submitButton = editForm.locator('button[type="submit"], input[type="submit"]').last();
  await submitButton.waitFor({ state: "visible", timeout: 20_000 });
  await Promise.all([
    page.waitForLoadState("networkidle").catch(() => undefined),
    submitButton.click(),
  ]);
}

async function createBrowserContext(storageState) {
  const contextOptions = storageState
    ? {
        storageState,
      }
    : {};

  const browserName = optionalEnv("GAMEBANANA_BROWSER") || "cloakbrowser";
  if (browserName === "cloakbrowser") {
    const { launchContext } = await import("cloakbrowser");
    const context = await launchContext({
      headless: true,
      humanize: true,
      contextOptions,
    });
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
  return {
    context,
    close: () => browser.close(),
  };
}

function getStorageState() {
  const storageStatePath = optionalEnv("GAMEBANANA_STORAGE_STATE");
  if (storageStatePath) {
    return storageStatePath;
  }

  const storageStateGz = decodeJsonBase64(optionalEnv("GAMEBANANA_STORAGE_STATE_B64_GZ"), true);
  if (storageStateGz) {
    return storageStateGz;
  }

  const storageState = decodeJsonBase64(optionalEnv("GAMEBANANA_STORAGE_STATE_B64"));
  if (storageState) {
    return storageState;
  }

  return null;
}

async function createAuthenticatedBrowserSession() {
  const storageState = getStorageState();
  const browserSession = await createBrowserContext(storageState);
  const cookies = storageState ? [] : decodeCookieBundle(optionalEnv("GAMEBANANA_COOKIES"));

  if (cookies.length > 0) {
    await browserSession.context.addCookies(cookies);
  }

  if (!storageState && cookies.length === 0) {
    await browserSession.close();
    throw new Error(
      "GAMEBANANA_STORAGE_STATE, GAMEBANANA_STORAGE_STATE_B64_GZ, GAMEBANANA_STORAGE_STATE_B64, or GAMEBANANA_COOKIES is required.",
    );
  }

  return browserSession;
}

async function main() {
  const submissionId = requiredEnv("GAMEBANANA_SUBMISSION_ID");
  const apiSection = optionalEnv("GAMEBANANA_API_SECTION") || "Mod";
  const updateId = requiredEnv("GAMEBANANA_UPDATE_ID");
  const fileId = requiredEnv("GAMEBANANA_FILE_ID");
  const pageSection = optionalEnv("GAMEBANANA_PAGE_SECTION") || "mods";
  const notes = optionalEnv("GAMEBANANA_TRASH_NOTES") || "Removing an automated Akron release test.";

  const browserSession = await createAuthenticatedBrowserSession();
  const { context } = browserSession;
  try {
    await trashUpdateIfPresent(context.request, apiSection, submissionId, updateId, notes);

    const file = await getFile(context.request, apiSection, submissionId, fileId);
    if (!file) {
      console.log(`GameBanana file ${fileId} is already absent from the public file list.`);
    } else if (file._bIsArchived === true) {
      console.log(`GameBanana file ${fileId} is already archived.`);
    } else {
      const page = await context.newPage();
      try {
        await trashResource(context.request, "File", fileId, notes);
        console.log(`Trashed GameBanana file ${fileId}.`);
      } catch (error) {
        console.log(
          `Direct GameBanana file trash failed; archiving ${file._sFile} through the edit form instead.`,
        );
        await inspectFilesSection(page, pageSection, submissionId, fileId);
        await archiveFileThroughEditForm(page, pageSection, submissionId, file._sFile);
        console.log(`Archived GameBanana file ${fileId}.`);
      }
    }

    const update = await getUpdate(context.request, apiSection, submissionId, updateId);
    if (update && update._bIsTrashed !== true) {
      throw new Error(`GameBanana update ${updateId} is still visible and not marked trashed.`);
    }

    const remainingFile = await getFile(context.request, apiSection, submissionId, fileId);
    if (remainingFile && remainingFile._bIsArchived !== true) {
      throw new Error(`GameBanana file ${fileId} is still visible and not archived.`);
    }
  } finally {
    await browserSession.close();
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
