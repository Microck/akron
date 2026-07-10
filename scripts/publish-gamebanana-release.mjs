#!/usr/bin/env node
import { appendFile, mkdir, writeFile } from "node:fs/promises";
import { readFile, stat } from "node:fs/promises";
import { createHash } from "node:crypto";
import { basename } from "node:path";
import { pathToFileURL } from "node:url";
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

function findReleaseFile(files, version, releaseAssetInfo = null) {
  return getUpdateRecords(files)
    .filter((file) => releaseFileMatches(file, version))
    .filter((file) => !releaseAssetInfo || releaseFileMatchesAsset(file, releaseAssetInfo))
    .filter((file) => file._bIsArchived !== true)
    .sort((left, right) => Number(right._tsDateAdded ?? 0) - Number(left._tsDateAdded ?? 0))[0];
}

function activeFileIdsInDisplayOrder(files) {
  return getUpdateRecords(files)
    .filter((file) => file && typeof file === "object")
    .filter((file) => file._bIsArchived !== true)
    .map((file) => Number(file._idRow))
    .filter((fileId) => Number.isFinite(fileId));
}

function assertFirstActiveFile(files, expectedFileId) {
  const activeFileIds = activeFileIdsInDisplayOrder(files);
  const normalizedExpectedFileId = Number(expectedFileId);
  if (activeFileIds[0] === normalizedExpectedFileId) {
    return;
  }

  throw new Error(
    `GameBanana file ${normalizedExpectedFileId} was uploaded, but active file order is ${activeFileIds.join(", ") || "(none)"}. Refusing to publish with the newest release hidden below older files.`,
  );
}

function firstActiveFileMatches(files, expectedFileId) {
  return activeFileIdsInDisplayOrder(files)[0] === Number(expectedFileId);
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

function releaseFileMatchesAsset(file, releaseAssetInfo) {
  if (!file || typeof file !== "object" || !releaseAssetInfo) {
    return false;
  }

  const fileSize = Number(file._nFilesize);
  if (Number.isFinite(fileSize) && fileSize !== releaseAssetInfo.size) {
    return false;
  }

  const md5 = String(file._sMd5Checksum ?? "").toLowerCase();
  return md5.length === 0 || md5 === releaseAssetInfo.md5;
}

async function getReleaseAssetInfo(assetPath) {
  const [assetStat, assetBuffer] = await Promise.all([
    stat(assetPath),
    readFile(assetPath),
  ]);

  return {
    size: assetStat.size,
    md5: createHash("md5").update(assetBuffer).digest("hex"),
  };
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

async function waitForFirstActiveFile(page, request, apiSection, submissionId, expectedFileId) {
  let files = [];
  for (let attempt = 1; attempt <= 12; attempt += 1) {
    files = await fetchJson(
      request,
      `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Files`,
    );

    if (activeFileIdsInDisplayOrder(files)[0] === Number(expectedFileId)) {
      return;
    }

    await page.waitForTimeout(5_000);
  }

  assertFirstActiveFile(files, expectedFileId);
}

async function ensureFirstActiveFile(
  page,
  request,
  apiSection,
  pageSection,
  submissionId,
  file,
) {
  const fileId = Number(file?._idRow);
  if (!Number.isFinite(fileId)) {
    throw new Error("Cannot promote GameBanana file because its row ID is missing.");
  }

  const files = await fetchJson(
    request,
    `${gamebananaOrigin}/apiv11/${apiSection}/${submissionId}/Files`,
  );
  if (firstActiveFileMatches(files, fileId)) {
    return;
  }

  await promoteExistingReleaseFile(page, pageSection, submissionId, file);
  await waitForFirstActiveFile(page, request, apiSection, submissionId, fileId);
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

async function uploadReleaseAssetAndFindFileId(
  page,
  request,
  apiSection,
  pageSection,
  submissionId,
  releaseAsset,
  hasStoredAuth,
  username,
  password,
  cookieFallbackAuth = null,
) {
  const beforeIds = await getFileRowIds(request, apiSection, submissionId);
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
      if (cookieFallbackAuth) {
        console.log(
          "Stored GameBanana session could not edit the submission; retrying with GAMEBANANA_COOKIES.",
        );
        await cookieFallbackAuth(page);
        try {
          await uploadReleaseAsset(page, pageSection, submissionId, releaseAsset);
        } catch (cookieError) {
          if (
            cookieError instanceof Error &&
            cookieError.code === "GAMEBANANA_EDIT_PERMISSION_DENIED"
          ) {
            await logPageState(page, "GameBanana cookie auth edit denial");
            await captureDebugArtifact(page, "gamebanana-cookie-auth-edit-denial");
          } else {
            await logPageState(page, "GameBanana cookie auth upload failed");
            await captureDebugArtifact(page, "gamebanana-cookie-auth-upload-failed");
            throw cookieError;
          }

          if (!username || !password) {
            throw cookieError;
          }

          console.log("GAMEBANANA_COOKIES could not edit the submission; retrying with credentials.");
          await logIn(page, username, password);
          await uploadReleaseAsset(page, pageSection, submissionId, releaseAsset);
        }
      } else {
        console.log("Stored GameBanana session could not edit the submission; retrying with credentials.");
        await logIn(page, username, password);
        await uploadReleaseAsset(page, pageSection, submissionId, releaseAsset);
      }
    } else {
      await logPageState(page, "GameBanana upload failed");
      await captureDebugArtifact(page, "gamebanana-upload-failed");
      throw error;
    }
  }

  for (let attempt = 1; attempt <= 12; attempt += 1) {
    const afterIds = await getFileRowIds(request, apiSection, submissionId);
    const newIds = [...afterIds].filter((id) => !beforeIds.has(id));
    if (newIds.length > 0) {
      return Math.max(...newIds);
    }

    await page.waitForTimeout(5_000);
  }

  await logPageState(page, "GameBanana upload did not create a new file");
  await captureDebugArtifact(page, "gamebanana-upload-did-not-create-a-new-file");
  throw new Error(`Could not determine GameBanana file row ID for ${basename(releaseAsset)}.`);
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
  const fileInput = await openEditFileManager(page, pageSection, submissionId);
  const uploadedFileCount = await page.locator('#Files [id$="_UploadedFiles"] li').count();
  await fileInput.setInputFiles(assetPath);

  await page.waitForFunction(
    (previousCount) =>
      document.querySelectorAll('#Files [id$="_UploadedFiles"] li').length > previousCount &&
      document.querySelector("#Files .UploadMessage")?.textContent?.includes("Upload complete"),
    uploadedFileCount,
    { timeout: 120_000 },
  );

  const promotedFile = await promoteNewestUploadedFile(page, uploadedFileCount);
  console.log(
    `Promoted uploaded GameBanana file ${JSON.stringify(promotedFile.fileName)} from position ${promotedFile.previousIndex + 1} to 1.`,
  );

  await submitFileManager(page, fileInput);
}

async function promoteExistingReleaseFile(page, pageSection, submissionId, file) {
  const fileInput = await openEditFileManager(page, pageSection, submissionId);
  const promotedFile = await promoteExistingUploadedFile(page, file);
  console.log(
    `Promoted existing GameBanana file ${JSON.stringify(promotedFile.fileName)} from position ${promotedFile.previousIndex + 1} to 1.`,
  );
  await submitFileManager(page, fileInput);
}

async function openEditFileManager(page, pageSection, submissionId) {
  for (let attempt = 1; attempt <= 2; attempt += 1) {
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

    const uploadSlot = await ensureFileUploadSlotAvailable(fileInput);
    if (!uploadSlot.removedFile) {
      return fileInput;
    }

    console.log(
      `Removed ${uploadSlot.fileKind} GameBanana file ${JSON.stringify(uploadSlot.fileName)} to free an upload slot before publishing.`,
    );
    await submitFileManager(page, fileInput);
  }

  throw new Error("GameBanana file limit stayed full after removing an old file row.");
}

async function ensureFileUploadSlotAvailable(fileInput) {
  return fileInput.evaluate((input) => {
    const wrapper = input.closest(".InputWrapper");
    const list = wrapper?.querySelector('[id$="_UploadedFiles"]');
    const items = Array.from(list?.querySelectorAll("li") ?? []);
    const jQueryData = globalThis.jQuery ? globalThis.jQuery(input).data() : {};
    const slotCount = Number(jQueryData._nFileSlots ?? items.length);
    const uploadedCount = Number(jQueryData._nCurrentUploadedFiles ?? items.length);

    if (!Number.isFinite(slotCount) || uploadedCount < slotCount) {
      return { removedFile: false };
    }

    const archivedItem = items.find((item) =>
      item.querySelector('.ArchivedInput[type="checkbox"]:checked'),
    );
    const itemToRemove = archivedItem ?? items[items.length - 1];

    const fileName =
      itemToRemove.querySelector("code")?.textContent?.trim() ??
      itemToRemove.querySelector("[title]")?.getAttribute("title") ??
      itemToRemove.textContent?.replace(/\s+/g, " ").trim() ??
      "(unknown)";

    // The GameBanana edit list is newest-first. If there are no archived rows
    // left to prune, remove the oldest active row so the newest release can be
    // uploaded and promoted to the first active download.
    itemToRemove.remove();

    if (globalThis.jQuery) {
      const data = globalThis.jQuery(input).data();
      if (Number.isFinite(Number(data._nCurrentUploadedFiles))) {
        data._nCurrentUploadedFiles = Math.max(0, Number(data._nCurrentUploadedFiles) - 1);
      }
    }

    list?.dispatchEvent(new Event("input", { bubbles: true }));
    list?.dispatchEvent(new Event("change", { bubbles: true }));

    return { removedFile: true, fileKind: archivedItem ? "archived" : "oldest active", fileName };
  });
}

async function submitFileManager(page, fileInput) {
  const uploadForm = page.locator("form", { has: fileInput }).first();
  const submitButton = uploadForm.locator('button[type="submit"], input[type="submit"]').last();
  await submitButton.waitFor({ state: "visible", timeout: 20_000 });

  await serializeUploadedFileLists(uploadForm);
  await new Promise((resolve) => setTimeout(resolve, 2_000));

  const submitUrl = page.url().split("#")[0];
  const saveResponse = page
    .waitForResponse(
      (response) =>
        response.request().method() === "POST" &&
        response.url().startsWith(submitUrl),
      { timeout: 30_000 },
    )
    .catch(() => null);

  await uploadForm.evaluate((form) => {
    HTMLFormElement.prototype.submit.call(form);
  });
  await saveResponse;
  await page.waitForLoadState("networkidle").catch(() => undefined);
}

async function serializeUploadedFileLists(uploadForm) {
  await uploadForm.evaluate((form) => {
    function serializeInput(input) {
      if (!input.name) {
        return null;
      }

      if ((input.type === "checkbox" || input.type === "radio") && !input.checked) {
        return null;
      }

      return {
        name: input.name,
        value: input.value,
      };
    }

    for (const list of form.querySelectorAll('[id$="_UploadedFiles"]')) {
      const wrapper = list.closest(".InputWrapper");
      const hiddenInput = wrapper?.querySelector('input[type="hidden"][id]');
      if (!hiddenInput) {
        continue;
      }

      // GameBanana writes these hidden JSON fields from a submit click handler.
      // The automation mutates the upload list directly, so write the same
      // payload explicitly before Save to keep removals and reordering durable.
      const items = Array.from(list.querySelectorAll("li"));
      if (list.classList.contains("AdvancedUploadedFiles")) {
        hiddenInput.value = JSON.stringify(
          items.map((item) =>
            Array.from(item.querySelectorAll("input, textarea, select"))
              .map(serializeInput)
              .filter(Boolean),
          ),
        );
      } else {
        hiddenInput.value = JSON.stringify(
          items
            .flatMap((item) => Array.from(item.querySelectorAll("input, textarea, select")))
            .map(serializeInput)
            .filter(Boolean),
        );
      }
    }
  });
}

async function promoteExistingUploadedFile(page, file) {
  return promoteUploadedFileInList(page, { file });
}

async function promoteNewestUploadedFile(page, previousFileCount) {
  return promoteUploadedFileInList(page, { previousFileCount });
}

async function promoteUploadedFileInList(page, target) {
  return page.evaluate((target) => {
    function promoteUploadedListItem(list, uploadedItem) {
      const items = Array.from(list.querySelectorAll("li"));
      const previousIndex = items.indexOf(uploadedItem);
      const fileName =
        uploadedItem.querySelector("[title]")?.getAttribute("title") ??
        uploadedItem.querySelector("input[type='hidden'][value]")?.getAttribute("value") ??
        uploadedItem.textContent?.replace(/\s+/g, " ").trim() ??
        "(unknown)";

      if (previousIndex > 0) {
        list.insertBefore(uploadedItem, list.firstElementChild);
      }

      list.dispatchEvent(new Event("input", { bubbles: true }));
      list.dispatchEvent(new Event("change", { bubbles: true }));
      list.dispatchEvent(new CustomEvent("sortupdate", { bubbles: true }));

      if (globalThis.jQuery) {
        globalThis.jQuery(list).trigger("sortupdate").trigger("change");
      }

      const firstItem = list.querySelector("li");
      if (firstItem !== uploadedItem) {
        throw new Error("GameBanana uploaded file could not be moved to the top of the upload list.");
      }

      return {
        fileName,
        previousIndex,
      };
    }

    const list = document.querySelector('#Files [id$="_UploadedFiles"]');
    if (!list) {
      throw new Error("GameBanana uploaded-files list is missing.");
    }

    const items = Array.from(list.querySelectorAll("li"));
    let uploadedItem = null;

    if ("previousFileCount" in target) {
      if (items.length <= target.previousFileCount) {
        throw new Error(
          `GameBanana uploaded-files list has ${items.length} rows, expected more than ${target.previousFileCount}.`,
        );
      }

      // GameBanana renders profile files in stored list order. Uploads append
      // to this edit-list, so move the newly uploaded row before submitting.
      const newItems = items.slice(target.previousFileCount);
      uploadedItem = newItems[newItems.length - 1];
    } else {
      const fileId = String(target.file?._idRow ?? "");
      const fileName = String(target.file?._sFile ?? "");
      uploadedItem = items.find((item) => {
        const values = [
          item.id,
          item.textContent,
          ...Array.from(item.querySelectorAll("[href], [src], [value], [name], [id]")).flatMap(
            (element) => [
              element.getAttribute("href"),
              element.getAttribute("src"),
              element.getAttribute("value"),
              element.getAttribute("name"),
              element.getAttribute("id"),
            ],
          ),
        ]
          .filter((value) => typeof value === "string")
          .map((value) => value.trim());

        return values.some((value) => value.includes(fileId)) ||
          (fileName.length > 0 && values.some((value) => value.includes(fileName)));
      });

      if (!uploadedItem) {
        throw new Error(`Could not find GameBanana file row ${fileId || fileName} in the upload list.`);
      }
    }

    return promoteUploadedListItem(list, uploadedItem);
  }, target);
}

function redactKnownSecrets(value) {
  let redacted = value;
  for (const name of [
    "GAMEBANANA_USERNAME",
    "GAMEBANANA_PASSWORD",
    "GAMEBANANA_COOKIES",
    "GAMEBANANA_STORAGE_STATE_B64",
    "GAMEBANANA_STORAGE_STATE_B64_GZ",
  ]) {
    const secret = process.env[name];
    if (secret) {
      redacted = redacted.replaceAll(secret, `[${name}]`);
    }
  }
  return redacted;
}

function sanitizedPageUrl(value) {
  try {
    const url = new URL(value);
    return `${url.origin}${url.pathname}`;
  } catch {
    return "[invalid URL]";
  }
}

async function logPageState(page, label) {
  const [title, hasEditForm, hasFileInput, hasPasswordInput] = await Promise.all([
    page.title().catch(() => ""),
    page.locator("#EditFormModule").first().isVisible().catch(() => false),
    page.locator('input[type="file"]').first().isVisible().catch(() => false),
    page.locator('input[type="password"]').first().isVisible().catch(() => false),
  ]);

  console.log(
    `${label}: url=${sanitizedPageUrl(page.url())} title=${JSON.stringify(redactKnownSecrets(title).slice(0, 200))} editForm=${hasEditForm} fileInput=${hasFileInput} passwordInput=${hasPasswordInput}`,
  );
}

async function captureDebugArtifact(page, label) {
  const debugDir = process.env.GAMEBANANA_DEBUG_DIR;
  if (!debugDir) {
    return;
  }

  await mkdir(debugDir, { recursive: true });
  const slug = label.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  const [title, forms] = await Promise.all([
    page.title().catch(() => ""),
    page.locator("form").evaluateAll((formElements) =>
      formElements.map((form, formIndex) => ({
        formIndex,
        method: form.getAttribute("method"),
        fileInputs: Array.from(form.querySelectorAll('input[type="file"]')).map((input) => ({
          name: input.getAttribute("name"),
          id: input.getAttribute("id"),
          fileCount: input.files?.length ?? 0,
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
          visible:
            control instanceof HTMLElement &&
            control.offsetParent !== null &&
            getComputedStyle(control).visibility !== "hidden",
        })),
      })),
    ).catch(() => [{ collectionFailed: true }]),
  ]);

  await writeFile(
    `${debugDir}/${slug}-summary.json`,
    JSON.stringify({
      label,
      url: sanitizedPageUrl(page.url()),
      title: redactKnownSecrets(title).slice(0, 200),
      forms,
    }, null, 2),
    "utf8",
  );
}

async function setGithubOutput(name, value) {
  const outputPath = process.env.GITHUB_OUTPUT;
  if (!outputPath) {
    return;
  }

  await appendFile(outputPath, `${name}=${value}\n`, "utf8");
}

async function createBrowserContext(browserName, storageState, userAgent) {
  const contextOptions = {
    ...(storageState ? { storageState } : {}),
    ...(userAgent ? { userAgent } : {}),
  };

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
  const userAgent = optionalEnv("GAMEBANANA_USER_AGENT", "");
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

  const releaseAssetInfo = await getReleaseAssetInfo(releaseAsset);
  const releaseNotes = await readFile(releaseNotesFile, "utf8");
  const version = releaseTag.replace(/^v/, "");
  const releaseDetails = {
    name: releaseName,
    notes: releaseNotes,
    version,
  };
  const cookies = decodeCookieBundle(cookieBundle);

  const browserSession = await createBrowserContext(browserName, storageState, userAgent);
  const { context } = browserSession;
  const initialCookies = storageState ? [] : cookies;
  const cookieFallbackAuth = storageState && cookies.length > 0
    ? async (page) => {
        const pageContext = page.context();
        await pageContext.clearCookies();
        await pageContext.addCookies(cookies);
        await page.goto(gamebananaOrigin, { waitUntil: "domcontentloaded" });
      }
    : null;
  if (storageState && cookies.length > 0) {
    console.log(
      "Using GAMEBANANA_STORAGE_STATE; keeping GAMEBANANA_COOKIES as a fallback if stored auth cannot edit.",
    );
  }
  if (initialCookies.length > 0) {
    await context.addCookies(initialCookies);
    console.log(`Loaded ${initialCookies.length} GameBanana cookies from GAMEBANANA_COOKIES.`);
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
        releaseFileMatches(findFileById(files, fileId), version) &&
        releaseFileMatchesAsset(findFileById(files, fileId), releaseAssetInfo),
      );

      if (!matchingLinkedFileId) {
        const replacementFile = findReleaseFile(files, version, releaseAssetInfo);
        const replacementFileId = replacementFile
          ? Number(replacementFile._idRow)
          : await uploadReleaseAssetAndFindFileId(
            page,
            context.request,
            apiSection,
            pageSection,
            submissionId,
            releaseAsset,
            hasStoredAuth,
            username,
            password,
            cookieFallbackAuth,
          );

        if (replacementFile) {
          await ensureFirstActiveFile(
            page,
            context.request,
            apiSection,
            pageSection,
            submissionId,
            replacementFile,
          );
        } else {
          await waitForFirstActiveFile(page, context.request, apiSection, submissionId, replacementFileId);
        }

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

      const matchingLinkedFile = findFileById(files, matchingLinkedFileId);
      await ensureFirstActiveFile(
        page,
        context.request,
        apiSection,
        pageSection,
        submissionId,
        matchingLinkedFile,
      );
      await setGithubOutput("file_id", matchingLinkedFileId);
      console.log(
        `GameBanana update ${existingReleaseUpdate._idRow ?? "(unknown id)"} already exists for ${releaseTag}.`,
      );
      console.log(`GameBanana file ID: ${matchingLinkedFileId}`);
      return;
    }

    const uploadedFileId = await uploadReleaseAssetAndFindFileId(
      page,
      context.request,
      apiSection,
      pageSection,
      submissionId,
      releaseAsset,
      hasStoredAuth,
      username,
      password,
      cookieFallbackAuth,
    );

    await waitForFirstActiveFile(page, context.request, apiSection, submissionId, uploadedFileId);

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

    await waitForFirstActiveFile(page, context.request, apiSection, submissionId, uploadedFileId);

    await setGithubOutput("file_id", uploadedFileId);
    console.log(`Published ${releaseTag} to GameBanana submission ${submissionId}.`);
    console.log(`GameBanana file ID: ${uploadedFileId}`);
  } finally {
    await browserSession.close();
  }
}

export {
  activeFileIdsInDisplayOrder,
  assertFirstActiveFile,
  firstActiveFileMatches,
  promoteUploadedFileInList,
  releaseFileMatchesAsset,
};

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.message : error);
    process.exit(1);
  });
}
