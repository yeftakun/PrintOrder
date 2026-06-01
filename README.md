# PrintOrder

## Pairing model (desktop client)

- Tombol utama di desktop sekarang memakai mode **Pair Akun / Lepas Pairing** (bukan login/logout harian).
- Pairing dilakukan sekali dengan `identifier` + `password` akun mitra lewat endpoint `POST /api/clients/:id/pair`.
- Setelah itu app tidak perlu minta password lagi di setiap startup selama refresh token masih valid.
- Lepas pairing akan memanggil `POST /api/clients/:id/unbind`.
- Lepas pairing dari desktop mewajibkan verifikasi PIN akun (atur PIN di `/mitra/account/`).

## Lokasi file lokal

- Konfigurasi dan state auth disimpan per-user:
  - `%LocalAppData%\\PrintOrder\\printorder.ini`
  - `%LocalAppData%\\PrintOrder\\printorder.auth.json`
- ID client disimpan machine-level (shared antar user Windows):
  - `%ProgramData%\\PrintOrder\\printorder.client-id`
- Migrasi otomatis saat startup:
  - `printorder.ini` dan `printorder.auth.json`: dari lokasi lama ke `%LocalAppData%`.
  - `printorder.client-id`: prioritas dari lokasi machine-level lama, lokasi user lama, fallback dari folder executable lama, lalu disalin ke `%ProgramData%`.

build & run

```powershell
dotnet build .\PrintOrder\PrintOrder.csproj -c Release
```

```powershell
dotnet run --project .\PrintOrder\PrintOrder.csproj
```

release publish (target for installer)

```powershell
dotnet publish .\PrintOrder\PrintOrder.csproj -c Release -f net8.0-windows -o .\artifacts\publish
```

build installer (inno setup)

Optional: to bundle SumatraPDF as a prerequisite, put the official installer at `installer\SumatraPDF-Installer.exe` before running Inno Setup.

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\PrintOrder.iss
```

installer output

```text
.\artifacts\installer\PrintOrder-Setup-1.3.0.exe
```
