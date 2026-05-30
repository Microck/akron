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

async function trashResource(context, section, id, notes) {
  await fetchJson(context, `${gamebananaOrigin}/apiv12/${section}/${id}`, {
    method: "DELETE",
    data: {
      _idReasonRow: "2",
      _sNotes: notes,
    },
  });
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
  const notes = optionalEnv("GAMEBANANA_TRASH_NOTES") || "Removing an automated Akron release test.";

  const browserSession = await createAuthenticatedBrowserSession();
  const { context } = browserSession;
  try {
    await trashResource(context.request, "Update", updateId, notes);
    console.log(`Trashed GameBanana update ${updateId}.`);

    await trashResource(context.request, "File", fileId, notes);
    console.log(`Trashed GameBanana file ${fileId}.`);

    const updates = await fetchJson(
      context.request,
      `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Updates`,
    );
    const update = updateRecords(updates).find((record) => Number(record?._idRow) === Number(updateId));
    if (update && update._bIsTrashed !== true) {
      throw new Error(`GameBanana update ${updateId} is still visible and not marked trashed.`);
    }
  } finally {
    await browserSession.close();
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
