# Direct-content tool dependencies

The development content pipeline installs `PyFFI 2.2.3` from PyPI. PyFFI is
distributed under a BSD-style three-clause license; its source is not vendored
in this repository. Binary distributions of the content compiler must retain
PyFFI's copyright, conditions, and disclaimer.

PyFFI is a file-format library. It is not a game engine, runtime oracle, or
source of generated OpenNV assets.

Experimental packages build the command-line content helper with PyInstaller
6.22.2. PyInstaller is GPLv2 with its bootloader exception; packaged builds must
retain the license and exception terms supplied by that project. PyInstaller is
a packaging tool and is not part of OpenNV's runtime behavior or data model.

The one-file helper embeds the CPython 3.11 runtime and setuptools support used
by PyFFI. Packaged outputs include the exact installed license texts for
CPython, setuptools, PyFFI, Pillow, and PyInstaller in their `licenses`
directory.

Pillow 12.3.0 decodes the player's DDS texture members into portable PNG cache
artifacts. Pillow is distributed under the HPND license. It is a file decoder,
not a game engine or source of OpenNV assets.
