# Third-party software and assets

AppSwitcher's own source, documentation and project artwork are distributed under [MIT](LICENSE). Third-party components retain their licenses and notices. Browser and Windows product names identify compatibility; they do not imply endorsement.

The complete exact-version inventory, copyright holders and upstream source commits are in [third-party/dependencies.json](third-party/dependencies.json). The release builder compares that inventory against both published applications' dependency manifests and stops on an unreviewed dependency or version.

| Component | Version | License and retained notices |
| --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.0 | [MIT](third-party/CommunityToolkit-MIT.txt), [notices](third-party/CommunityToolkit-NOTICES.txt) |
| Microsoft.Extensions.DependencyInjection, DependencyInjection.Abstractions, Logging, Logging.Abstractions, Options, Primitives | 10.0.5 | [MIT](third-party/dotnet-MIT.txt), [notices](third-party/Microsoft.Extensions-NOTICES.txt) |
| System.Management | 9.0.5 | [MIT](third-party/dotnet-MIT.txt), [notices](third-party/System.Management-NOTICES.txt) |
| Serilog | 4.2.0 | [Apache 2.0](third-party/serilog-LICENSE.txt); Copyright Serilog Contributors |
| Serilog.Extensions.Logging | 10.0.0 | [Apache 2.0](third-party/serilog-extensions-logging-LICENSE.txt); Copyright Serilog Contributors |
| Serilog.Sinks.File | 7.0.0 | [Apache 2.0](third-party/serilog-sinks-file-LICENSE.txt); Copyright Serilog Contributors |
| .NET and Windows Desktop runtime, Windows x64 | 10.0.14 | [MIT](third-party/dotnet-runtime-MIT.txt), [.NET notices](third-party/dotnet-runtime-NOTICES.txt), [WPF notices](third-party/wpf-NOTICES.txt), [Windows Forms notices](third-party/winforms-NOTICES.txt) |
| Inno Setup installer engine/compiler | 7.1.1 | [Inno Setup license](third-party/InnoSetup-LICENSE.txt), Copyright 1997–2026 Jordan Russell; portions Copyright 2000–2026 Martijn Laan. The compiler is downloaded for builds and is not included in the release ZIP. |

MIT/notice files were retained from the locked NuGet packages. The three Serilog LICENSE files were retrieved from the exact repository commits recorded in the inventory; none of those three source trees has a separate NOTICE file. WPF and Windows Forms notices come from `dotnet/dotnet` commit `0427b9abbead3fa64c6655eb4290c537397131fa`, matching the Windows Desktop runtime package. Inno Setup's license comes from its `is-7_1_1` tag.

`Assets/AppSwitcher.png` is project artwork generated/redrawn with an image-generation tool; the recorded prompt is in `Assets/AppSwitcher-icon.prompt.txt`. The ICO and store PNGs are generated from that artwork. Documentation screenshots show synthetic demonstration data from this application. The browser bridge source is part of AppSwitcher; Mozilla signature metadata in the bundled Firefox XPI authenticates its distribution and is not project-authored code. System fonts and OS/browser icons are not redistributed as font or asset packages.

Build/test tools such as the .NET SDK, Node.js, GitHub Actions, xUnit and Playwright are not included in the application payload. Their own licenses apply to their use. Do not redistribute build caches, browser profiles or test runner directories as release files.
