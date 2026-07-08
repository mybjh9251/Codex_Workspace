from __future__ import annotations

from dataclasses import replace
from pathlib import Path
from typing import Optional
from xml.etree import ElementTree as ET

from models import AppConfig, SpaceTrackCredentials

_PLACEHOLDER_VALUES = {
    "",
    "YOUR_SPACE_TRACK_ID",
    "YOUR_SPACE_TRACK_PASSWORD",
    "SPACE_TRACK_ID",
    "SPACE_TRACK_PASSWORD",
}


def build_default_config(
    project_root: Path,
    input_path: Path | None = None,
) -> AppConfig:
    return AppConfig(
        project_root=project_root,
        input_satellite_path=_resolve_input_path(project_root, input_path),
        profile_path=project_root / "profile.xml",
        log_dir=project_root / "logs",
        sheet_name="Sheet1",
        sat_name_header="SAT_Name",
        norad_id_header="NORAD ID",
        request_timeout_seconds=30,
        user_agent="TLE_Download/1.0",
    )


def _resolve_input_path(project_root: Path, input_path: Path | None) -> Path:
    if input_path is not None:
        if input_path.is_absolute():
            return input_path
        return project_root / input_path

    xlsx_path = project_root / "Sat_List.xlsx"
    if xlsx_path.exists():
        return xlsx_path

    return project_root / "Sat_List.csv"


def load_profile(
    config: AppConfig,
) -> tuple[AppConfig, Optional[SpaceTrackCredentials], Optional[str]]:
    profile_path = config.profile_path
    if not profile_path.exists():
        return (
            config,
            None,
            f"Profile file was not found: {profile_path}. "
            "Space-Track credentials are unavailable; using CelesTrak only.",
        )

    tree = ET.parse(profile_path)
    root = tree.getroot()

    credentials = _parse_credentials(root)
    credential_warning = None
    if credentials is None:
        credential_warning = (
            "Space-Track account information is missing or still placeholder "
            "values in profile.xml; using CelesTrak only."
        )

    timeout_text = _clean_text(root.findtext("./Options/RequestTimeoutSeconds"))
    user_agent = _clean_text(root.findtext("./Options/UserAgent")) or config.user_agent

    timeout_seconds = config.request_timeout_seconds
    if timeout_text:
        timeout_seconds = int(timeout_text)

    updated_config = replace(
        config,
        request_timeout_seconds=timeout_seconds,
        user_agent=user_agent,
    )
    return updated_config, credentials, credential_warning


def _parse_credentials(root: ET.Element) -> Optional[SpaceTrackCredentials]:
    identity = _clean_text(root.findtext("./SpaceTrack/ID"))
    password = _clean_text(root.findtext("./SpaceTrack/PW"))

    if _is_placeholder(identity) or _is_placeholder(password):
        return None

    return SpaceTrackCredentials(identity=identity, password=password)


def _clean_text(value: Optional[str]) -> str:
    if value is None:
        return ""
    return value.replace("\u00a0", " ").strip()


def _is_placeholder(value: str) -> bool:
    return value.strip() in _PLACEHOLDER_VALUES
