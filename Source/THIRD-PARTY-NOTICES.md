# Third-party notices

The MDU Asset Server release archives and Docker images contain the following third-party components. Their license texts are in the `licenses` folder next to the program.

| Component | License | License text |
| --- | --- | --- |
| .NET runtime and ASP.NET Core (compiled into the program by Native AOT) | MIT | `licenses/MIT-dotnet.txt` |
| Microsoft.Data.Sqlite, Microsoft.AspNetCore.OpenApi | MIT | `licenses/MIT-dotnet.txt` |
| NetVips, by Kleis Auke Wolthuizen (compiled into the program) | MIT | <https://github.com/kleisauke/net-vips/blob/master/LICENSE> |
| libvips and the libraries bundled with it (`libvips.42.dylib` / `libvips.so.42`, from NetVips.Native) | LGPL-3.0-or-later for libvips; the bundled libraries are listed with their licenses in `licenses/libvips-THIRD-PARTY-NOTICES.md` | `licenses/LGPL-3.0.txt`, `licenses/GPL-3.0.txt` |
| SQLitePCLRaw (`libe_sqlite3`), by Eric Sink | Apache License 2.0 | `licenses/Apache-2.0.txt` |
| SQLite, compiled into `libe_sqlite3` | Public domain | <https://sqlite.org/copyright.html> |

libvips is loaded dynamically from the program folder and can be replaced with another build of the same version.

The release workflow and the Dockerfile copy `libvips-THIRD-PARTY-NOTICES.md` from the NetVips.Native package of the target platform. Update this file and the `Licenses` folder when a release starts to ship another third-party component.
