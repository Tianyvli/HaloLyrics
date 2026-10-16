# References and dependencies

The application uses these NuGet packages under their respective licenses:

- HidSharp 2.6.4 — https://www.nuget.org/packages/HidSharp/2.6.4
- Microsoft.Web.WebView2 1.0.4258.31 — https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4258.31
- Microsoft .NET 10 runtime and Windows desktop runtime — https://github.com/dotnet/runtime and https://github.com/dotnet/winforms

The wire format was compared with these independent public implementations, then checked against the user's A160 hardware. No vendor binaries or decompiled vendor code are included in the release:

- XFEstudio/HaloPixelToolBox — https://github.com/XFEstudio/HaloPixelToolBox (MIT)
- Seraph310/halo-pixelbar-mcp — https://github.com/Seraph310/halo-pixelbar-mcp (MIT)

The Apple web lyrics endpoint and response shape were cross-checked with:

- anselmoshim/applemusic-private — https://github.com/anselmoshim/applemusic-private

Application code is independently implemented. Dependency package licenses remain applicable; copies are included in the release's `licenses` folder. Apple and EDIFIER names identify the compatible products.
