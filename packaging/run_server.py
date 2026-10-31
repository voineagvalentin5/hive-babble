# Hive-Babble bundled translator entry point (unmodified LibreTranslate, run as a separate local process).
import logging
import os
from pathlib import Path

here = Path(__file__).resolve().parent
state = Path(os.environ.get("LOCALAPPDATA", str(here))) / "Hive-Babble" / "translator"
os.environ["ARGOS_PACKAGES_DIR"] = str(here / "models")
for var, sub in (("XDG_DATA_HOME", "data"), ("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache")):
    os.environ[var] = str(state / sub)
    (state / sub).mkdir(parents=True, exist_ok=True)
os.environ["ARGOS_DEBUG"] = "0"  # keep screen text out of diagnostic logs

import minisbd.models
minisbd.models.cache_dir = str(state / "cache" / "minisbd")
from libretranslate.main import main

logging.getLogger("argostranslate.utils").setLevel(logging.WARNING)
main()
