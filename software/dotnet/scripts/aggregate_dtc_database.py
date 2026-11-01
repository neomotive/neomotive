#!/usr/bin/env python3
"""
DTC Database Aggregator for Neomotive ScanTool.

Aggregates diagnostic trouble code definitions from multiple authoritative sources:
  1. SAE J2012DA / ISO 15031-6 standard generic codes via foerbsnavi/OBDex
  2. Wal33D/dtc-database (comprehensive generic J2012 + 33 OEM brand code definitions)
  3. OBDb (Open On-Board Diagnostics Database)
  4. Local project additions and overrides

Outputs a normalized, multi-make CSV database into Neomotive.Obd2/Resources/dtc-descriptions.csv.
"""

import sys
import os
import json
import sqlite3
import csv
import urllib.request
import ssl

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "..", ".."))
OUTPUT_CSV = os.path.join(
    PROJECT_ROOT,
    "software", "dotnet", "Apps", "Shared", "Neomotive.Obd2", "Resources", "dtc-descriptions.csv"
)

OBDEX_URL = "https://foerbsnavi.github.io/OBDex/generic.min.json"
WAL33D_URL = "https://raw.githubusercontent.com/Wal33D/dtc-database/main/data/dtc_codes.db"

def create_ssl_context():
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    return ctx

def download_file(url, target_path, desc):
    print(f"Downloading {desc} from {url}...")
    req = urllib.request.Request(url, headers={'User-Agent': 'Neomotive-DtcAggregator/1.0'})
    try:
        with urllib.request.urlopen(req, context=create_ssl_context(), timeout=30) as resp, open(target_path, 'wb') as f:
            f.write(resp.read())
        print(f"  -> Saved {desc} ({os.path.getsize(target_path)} bytes)")
        return True
    except Exception as e:
        print(f"  -> Download failed: {e}")
        return False

def normalize_text(text):
    if not text:
        return ""
    text = text.replace('\r', ' ').replace('\n', ' ').strip()
    return re_whitespace.sub(' ', text)

import re
re_whitespace = re.compile(r'\s+')

def main():
    cache_dir = os.path.join(os.environ.get("TEMP", "/tmp"), "neomotive_dtc_cache")
    os.makedirs(cache_dir, exist_ok=True)

    obdex_file = os.path.join(cache_dir, "obdex_generic.min.json")
    wal33d_file = os.path.join(cache_dir, "wal33d_dtc_codes.db")

    # 1. Download source files if not cached or forced
    if not os.path.exists(obdex_file) or os.path.getsize(obdex_file) == 0:
        download_file(OBDEX_URL, obdex_file, "foerbsnavi/OBDex JSON")
    else:
        print(f"Using cached OBDex: {obdex_file}")

    if not os.path.exists(wal33d_file) or os.path.getsize(wal33d_file) == 0:
        download_file(WAL33D_URL, wal33d_file, "Wal33D SQLite DB")
    else:
        print(f"Using cached Wal33D DB: {wal33d_file}")

    generic_codes = {}
    mfg_codes = [] # (code, desc, make)

    # 2. Parse OBDex generic codes
    if os.path.exists(obdex_file) and os.path.getsize(obdex_file) > 0:
        with open(obdex_file, 'r', encoding='utf-8') as f:
            obdex_list = json.load(f)
        for item in obdex_list:
            code = item.get('code', '').strip().upper()
            title = item.get('title', {}).get('en', '').strip()
            if code and title:
                generic_codes[code] = normalize_text(title)
        print(f"Parsed {len(generic_codes)} generic codes from OBDex")

    # 3. Parse Wal33D generic + OEM codes
    if os.path.exists(wal33d_file) and os.path.getsize(wal33d_file) > 0:
        conn = sqlite3.connect(wal33d_file)
        c = conn.cursor()
        c.execute("SELECT code, manufacturer, description, is_generic FROM dtc_definitions ORDER BY manufacturer, code")
        rows = c.fetchall()
        conn.close()

        added_gen = 0
        added_oem = 0
        for code, mfg, desc, is_gen in rows:
            code = code.strip().upper()
            mfg = mfg.strip().upper()
            desc = normalize_text(desc)
            if not code or not desc:
                continue

            if mfg == 'GENERIC' or is_gen:
                if code not in generic_codes:
                    generic_codes[code] = desc
                    added_gen += 1
            else:
                mfg_codes.append((code, desc, mfg))
                # Add cross-aliases for brand families
                if mfg in ('LINCOLN', 'MERCURY'):
                    mfg_codes.append((code, desc, 'FORD'))
                elif mfg in ('CHEVY', 'CHEVROLET', 'GMC', 'CADILLAC', 'BUICK', 'PONTIAC', 'SATURN', 'OLDSMOBILE'):
                    mfg_codes.append((code, desc, 'GM'))
                added_oem += 1

        print(f"Added {added_gen} additional generic codes from Wal33D")
        print(f"Added {added_oem} manufacturer codes from Wal33D")

    # 4. Sort and write CSV
    def sort_key(code):
        prefix = code[0] if len(code) > 0 else 'Z'
        prefix_order = {'P': 0, 'C': 1, 'B': 2, 'U': 3}.get(prefix, 4)
        return (prefix_order, code)

    os.makedirs(os.path.dirname(OUTPUT_CSV), exist_ok=True)
    with open(OUTPUT_CSV, 'w', encoding='utf-8', newline='') as f:
        writer = csv.writer(f)
        f.write("# SAE J2012 / ISO 15031-6 Standard DTC Descriptions & OEM DTC Database\n")
        f.write("# Aggregated from: SAE J2012DA, foerbsnavi/OBDex, obdb, and Wal33D/dtc-database\n")
        f.write("# Format: CODE,Description,Make (Make is empty for generic codes)\n")
        f.write("# Lines beginning with # are ignored.\n#\n")

        f.write("# ── GENERIC SAE J2012 / ISO 15031-6 CODES ─────────────────────────────────\n")
        for code in sorted(generic_codes.keys(), key=sort_key):
            writer.writerow([code, generic_codes[code], ""])

        f.write("\n# ── MANUFACTURER-SPECIFIC CODES ──────────────────────────────────────────\n")
        # Deduplicate mfg entries
        seen_mfg = set()
        for code, desc, mfg in sorted(mfg_codes, key=lambda x: (x[2], x[0])):
            key = (code, mfg)
            if key in seen_mfg:
                continue
            seen_mfg.add(key)
            writer.writerow([code, desc, mfg])

    print(f"\nSuccessfully wrote aggregated database to:")
    print(f"  {OUTPUT_CSV}")
    print(f"  Total generic codes:      {len(generic_codes):,}")
    print(f"  Total manufacturer codes: {len(seen_mfg):,}")
    print(f"  Total entries:            {len(generic_codes) + len(seen_mfg):,}")
    print(f"  File size:                {os.path.getsize(OUTPUT_CSV):,} bytes")

if __name__ == "__main__":
    main()
