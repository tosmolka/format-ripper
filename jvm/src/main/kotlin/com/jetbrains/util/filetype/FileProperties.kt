package com.jetbrains.util.filetype

enum class FileProperties(v: Long) {
  UnknownType(0x0),
  ExecutableType(0x1),
  SharedLibraryType(0x2),
  BundleType(0x3),
  TypeMask(0xFF),
  MultiArch(0x20000000),
  Managed(0x40000000),
  Signed(0x80000000)
}

