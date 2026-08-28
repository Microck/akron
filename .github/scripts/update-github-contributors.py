#!/usr/bin/env python3
from __future__ import annotations

import html
import json
import os
import sys
from pathlib import Path
from urllib.parse import parse_qsl, urlencode, urljoin, urlsplit, urlunsplit
from urllib.request import Request, urlopen


MARKER_START = "{/* BEGIN GENERATED GITHUB CONTRIBUTORS */}"
MARKER_END = "{/* END GENERATED GITHUB CONTRIBUTORS */}"
PAGE_PATH = Path("docs/project/special-thanks.mdx")


def contributor_url() -> str:
    repository = os.environ.get("GITHUB_REPOSITORY", "Microck/Akron")
    return f"https://api.github.com/repos/{repository}/contributors?{urlencode({'per_page': 100, 'anon': 'false'})}"


def fetch_contributors() -> list[dict[str, object]]:
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "Akron-github-contributors-updater",
    }
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"

    contributors = []
    next_url = contributor_url()
    while next_url:
        request = Request(next_url, headers=headers)
        with urlopen(request, timeout=30) as response:
            page = json.load(response)
            link_header = response.headers.get("Link", "")

        if not isinstance(page, list):
            raise ValueError("GitHub contributors response was not a list")
        contributors.extend(page)
        next_url = next_link(link_header)

    return [
        contributor
        for contributor in contributors
        if isinstance(contributor, dict)
        and contributor.get("type") == "User"
        and isinstance(contributor.get("login"), str)
        and isinstance(contributor.get("html_url"), str)
        and isinstance(contributor.get("avatar_url"), str)
    ]


def next_link(link_header: str) -> str | None:
    for link in link_header.split(","):
        url_part, _, relation_part = link.partition(";")
        if 'rel="next"' in relation_part:
            return urljoin("https://api.github.com", url_part.strip().strip("<>"))
    return None


def sized_avatar_url(avatar_url: str) -> str:
    parts = urlsplit(avatar_url)
    query = dict(parse_qsl(parts.query, keep_blank_values=True))
    query["size"] = "96"
    return urlunsplit((parts.scheme, parts.netloc, parts.path, urlencode(query), parts.fragment))


def render_contributors(contributors: list[dict[str, object]]) -> str:
    entries = []
    for contributor in contributors:
        login = html.escape(str(contributor["login"]), quote=True)
        profile_url = html.escape(str(contributor["html_url"]), quote=True)
        avatar_url = html.escape(sized_avatar_url(str(contributor["avatar_url"])), quote=True)
        entries.append(
            f'<a href="{profile_url}" className="github-contributor-avatar">'
            f'<img src="{avatar_url}" alt="{login}" '
            'width="48" height="48" style={{ borderRadius: "50%" }} /></a>'
        )

    if not entries:
        raise ValueError("GitHub returned no profile-backed contributors")

    return "\n".join(
        [
            MARKER_START,
            '<div className="github-contributor-list" style={{ display: "flex", flexWrap: "wrap", gap: "0.75rem" }}>',
            *entries,
            "</div>",
            MARKER_END,
        ]
    )


def update_page(page: str, generated: str) -> str:
    start = page.find(MARKER_START)
    end = page.find(MARKER_END)
    if (start == -1) != (end == -1):
        raise ValueError("Generated contributor markers are incomplete")

    if start != -1:
        end += len(MARKER_END)
        return page[:start] + generated + page[end:]

    heading = "## GitHub contributors\n\n"
    insertion = f"{heading}{generated}\n\n"
    frontmatter_end = page.find("---", 3)
    if frontmatter_end == -1:
        raise ValueError("Could not find the page frontmatter")
    insertion_point = frontmatter_end + len("---\n")
    return page[:insertion_point] + "\n" + insertion + page[insertion_point:]


def main() -> int:
    try:
        contributors = fetch_contributors()
        page = PAGE_PATH.read_text(encoding="utf-8")
        updated = update_page(page, render_contributors(contributors))
        PAGE_PATH.write_text(updated, encoding="utf-8")
        print(f"Updated {PAGE_PATH} with {len(contributors)} GitHub contributors.")
    except Exception as error:
        print(f"update-github-contributors: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
