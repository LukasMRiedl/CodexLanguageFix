# Third-party notices

## .NET Runtime grapheme segmentation

The grapheme-boundary state machine in `src/core_unicode.cpp` is adapted from
`src/libraries/System.Private.CoreLib/src/System/Text/Unicode/TextSegmentationUtility.cs`
and `src/libraries/System.Private.CoreLib/src/System/Text/Unicode/GraphemeClusterBreakType.cs`
in .NET Runtime tag `v8.0.0` (commit
[`5535e31a712343a63f5d7d796cd874e563e5ac14`](https://github.com/dotnet/runtime/commit/5535e31a712343a63f5d7d796cd874e563e5ac14)).
The adaptation preserves the .NET 8 UAX #29 revision 35 rules and queries grapheme
properties through the Windows ICU C API. The original .NET Runtime MIT license is
included at `third_party/dotnet/LICENSE.TXT`.
