from __future__ import annotations

from pathlib import Path

from clients import CelesTrakClient, SpaceTrackClient
from config import build_default_config, load_profile
from excel_reader import read_satellite_requests
from logging_utils import setup_daily_logger
from models import SatelliteRequest, TleLines, TleRecord
from writer import timestamped_output_path, write_tle_output


def run(project_root: Path, input_path: Path | None = None) -> int:
    config = build_default_config(project_root, input_path=input_path)
    output_path = timestamped_output_path(project_root)
    logger, log_path = setup_daily_logger(config.log_dir)

    logger.info("Starting TLE download run.")
    logger.info("Input file: %s", config.input_satellite_path)
    logger.info("Output file: %s", output_path)

    try:
        config, credentials, credential_warning = load_profile(config)
        if credential_warning:
            logger.warning(credential_warning)

        requests = read_satellite_requests(config, logger)
        logger.info("Loaded %s valid satellite rows.", len(requests))

        if not requests:
            write_tle_output(output_path, [])
            logger.warning("No valid rows were found in the input file.")
            print(_build_summary(0, 0, [], log_path))
            return 0

        unique_norad_ids = _collect_unique_norad_ids(requests)
        tle_cache: dict[str, TleLines] = {}
        celestrak_client = CelesTrakClient(
            timeout_seconds=config.request_timeout_seconds,
            user_agent=config.user_agent,
        )

        if credentials is None:
            logger.warning(
                "Space-Track is disabled for this run. "
                "Continuing with CelesTrak lookup only."
            )
        else:
            space_track_client = SpaceTrackClient(
                credentials=credentials,
                timeout_seconds=config.request_timeout_seconds,
                user_agent=config.user_agent,
                logger=logger,
            )
            try:
                space_track_results = space_track_client.fetch_many(unique_norad_ids)
                tle_cache.update(space_track_results)
                logger.info(
                    "Space-Track returned TLEs for %s of %s unique NORAD IDs.",
                    len(space_track_results),
                    len(unique_norad_ids),
                )
            except Exception:
                logger.exception(
                    "Space-Track batch query failed. Continuing with CelesTrak fallback."
                )

        unresolved_ids = [
            norad_id for norad_id in unique_norad_ids if norad_id not in tle_cache
        ]
        if unresolved_ids:
            for norad_id in unresolved_ids:
                try:
                    tle_lines = celestrak_client.fetch_one(norad_id)
                except Exception:
                    logger.exception(
                        "CelesTrak lookup failed for NORAD ID %s.",
                        norad_id,
                    )
                    continue

                if tle_lines is None:
                    logger.warning(
                        "No CelesTrak TLE was returned for NORAD ID %s.",
                        norad_id,
                    )
                    continue

                tle_cache[norad_id] = tle_lines
                logger.info(
                    "CelesTrak fallback succeeded for NORAD ID %s.",
                    norad_id,
                )

        output_records: list[TleRecord] = []
        failed_requests: list[SatelliteRequest] = []
        name_resolution_cache: dict[str, tuple[str, TleLines] | None] = {}
        for request in requests:
            tle_lines = tle_cache.get(request.norad_id)
            corrected_norad_id = request.norad_id

            if tle_lines is None:
                cached_resolution = name_resolution_cache.get(request.sat_name)
                if (
                    cached_resolution is None
                    and request.sat_name not in name_resolution_cache
                ):
                    try:
                        corrected_norad_id = celestrak_client.resolve_norad_by_name(
                            request.sat_name
                        )
                    except Exception:
                        logger.exception(
                            "CelesTrak SAT_Name lookup failed for SAT_Name '%s'.",
                            request.sat_name,
                        )
                        corrected_norad_id = None

                    if corrected_norad_id is None:
                        logger.warning(
                            "SAT_Name fallback could not resolve SAT_Name '%s' "
                            "for input NORAD ID %s.",
                            request.sat_name,
                            request.norad_id,
                        )
                        name_resolution_cache[request.sat_name] = None
                    else:
                        tle_lines = tle_cache.get(corrected_norad_id)
                        if tle_lines is None:
                            try:
                                tle_lines = celestrak_client.fetch_one(
                                    corrected_norad_id
                                )
                            except Exception:
                                logger.exception(
                                    "CelesTrak TLE fetch failed after SAT_Name "
                                    "resolution for SAT_Name '%s' -> NORAD ID %s.",
                                    request.sat_name,
                                    corrected_norad_id,
                                )
                                tle_lines = None

                        if tle_lines is None:
                            logger.warning(
                                "SAT_Name fallback resolved SAT_Name '%s' to NORAD ID %s, "
                                "but no TLE was returned.",
                                request.sat_name,
                                corrected_norad_id,
                            )
                            name_resolution_cache[request.sat_name] = None
                        else:
                            name_resolution_cache[request.sat_name] = (
                                corrected_norad_id,
                                tle_lines,
                            )
                            logger.warning(
                                "SAT_Name fallback corrected NORAD ID %s -> %s for "
                                "SAT_Name '%s'.",
                                request.norad_id,
                                corrected_norad_id,
                                request.sat_name,
                            )

                elif cached_resolution is not None:
                    corrected_norad_id, tle_lines = cached_resolution

                if tle_lines is None:
                    failed_requests.append(request)
                    continue

            output_records.append(
                TleRecord(
                    sat_name=request.sat_name,
                    norad_id=corrected_norad_id,
                    line1=tle_lines.line1,
                    line2=tle_lines.line2,
                    source=tle_lines.source,
                )
            )

        write_tle_output(output_path, output_records)
        logger.info(
            "Wrote %s TLE records to %s.",
            len(output_records),
            output_path,
        )

        failed_ids = sorted({request.norad_id for request in failed_requests})
        if failed_ids:
            logger.warning("Failed NORAD IDs: %s", ", ".join(failed_ids))
        else:
            logger.info("All NORAD IDs were resolved successfully.")

        print(_build_summary(len(requests), len(output_records), failed_ids, log_path))
        return 0
    except Exception as exc:
        logger.exception("TLE download run failed.")
        print(f"Run failed: {exc}")
        print(f"See log: {log_path}")
        return 1


def _collect_unique_norad_ids(requests: list[SatelliteRequest]) -> list[str]:
    seen: set[str] = set()
    ordered_ids: list[str] = []
    for request in requests:
        if request.norad_id in seen:
            continue
        seen.add(request.norad_id)
        ordered_ids.append(request.norad_id)
    return ordered_ids


def _build_summary(
    total_rows: int,
    success_count: int,
    failed_ids: list[str],
    log_path: Path,
) -> str:
    lines = [
        "TLE download completed.",
        f"- Input rows processed: {total_rows}",
        f"- Output records written: {success_count}",
        f"- Failed NORAD IDs: {len(failed_ids)}",
        f"- Log file: {log_path}",
    ]
    if failed_ids:
        lines.append("- Failed list: " + ", ".join(failed_ids))
    return "\n".join(lines)
