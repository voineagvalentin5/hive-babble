Hive-Babble translator - corresponding source
=============================================

Hive-Babble (by Codex Hive Labs LLC) ships an offline translator that runs as a separate
program on your PC. It is LibreTranslate 1.9.6 plus its Python dependencies, run on the
official embeddable CPython 3.11.9 for Windows x64. None of these components were modified.

This archive holds the complete source for the copyleft components in that bundle:

  sdist/        source distributions as published on PyPI:
                libretranslate 1.9.6 (AGPL-3.0), argos-translate-files 1.4.4 (AGPL-3.0),
                LexiLang 1.0.7 (AGPL-3.0+), minisbd 0.9.5 (AGPL-3.0), pymupdf 2.0
                (AGPL-3.0 / Artifex commercial), pymupdf4llm 2.0 and pymupdf_layout 2.0
                (AGPL-3.0 placeholder packages), pysrt 1.1.2 (GPL-3.0), chardet 7.7.0
  licenses/     license texts and package metadata for those components
  hive-babble-glue/
                run_server.py (the launcher Hive-Babble starts: sets model/data folders and
                runs LibreTranslate's normal entry point) and the build scripts
  requirements-frozen.txt
                exact versions of every Python package in the bundle

Every other bundled package is under a permissive license (MIT, BSD, Apache-2.0, PSF, MPL-2.0);
see THIRD-PARTY-NOTICES.txt in the Hive-Babble install folder.

Rebuilding the translator
-------------------------
1. Install Python 3.11 x64 for Windows.
2. python -m venv venv
   venv\Scripts\python -m pip install -r requirements-frozen.txt
   (or install the packages in sdist\ with: pip install sdist\<file> --no-deps)
3. Start it the same way Hive-Babble does:
   venv\Scripts\python -E hive-babble-glue\run_server.py --host 127.0.0.1 --port 5000
4. To use your rebuilt translator with Hive-Babble, replace the "translator\python" folder
   in the install directory with your build, or run your build on port 5000 before starting
   Hive-Babble (it uses whatever answers on http://127.0.0.1:5000).

Upstream projects: https://github.com/LibreTranslate/LibreTranslate ,
https://github.com/argosopentech , https://github.com/pymupdf/PyMuPDF ,
https://github.com/byroot/pysrt

Questions: voineagvalentin@codex-hive.com