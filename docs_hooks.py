"""Copy branding assets into the MkDocs docs tree before each build."""

from __future__ import annotations

import shutil
from pathlib import Path

from mkdocs.config.defaults import MkDocsConfig

_ASSET_NAMES = ("logo.png", "icon.png", "social-preview.png")


def on_pre_build(config: MkDocsConfig) -> None:
    repo_root = Path(__file__).resolve().parent
    destination = Path(config.docs_dir) / "site-assets"
    destination.mkdir(parents=True, exist_ok=True)

    for name in _ASSET_NAMES:
        source = repo_root / "assets" / name
        if not source.is_file():
            raise FileNotFoundError(f"Missing branding asset: {source}")
        shutil.copy2(source, destination / name)
