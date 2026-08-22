"""OpenNV uses Pillow only for DDS input and PNG output."""

hiddenimports = ["PIL.DdsImagePlugin", "PIL.PngImagePlugin"]
excludedimports = ["numpy"]
