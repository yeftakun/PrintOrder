# PrintForm

## Pairing model (desktop client)

- Tombol utama di desktop sekarang memakai mode **Pair Akun / Lepas Pairing** (bukan login/logout harian).
- Pairing dilakukan sekali dengan `identifier` + `password` akun mitra lewat endpoint `POST /api/clients/:id/pair`.
- Setelah itu app tidak perlu minta password lagi di setiap startup selama refresh token masih valid.
- Lepas pairing akan memanggil `POST /api/clients/:id/unbind`.
- Lepas pairing dari desktop mewajibkan verifikasi PIN akun (atur PIN di `/mitra/account/`).

## Lokasi file lokal

- Konfigurasi dan state auth disimpan per-user:
  - `%LocalAppData%\\PrintForm\\printform.ini`
  - `%LocalAppData%\\PrintForm\\printform.auth.json`
- ID client disimpan machine-level (shared antar user Windows):
  - `%ProgramData%\\PrintForm\\printform.client-id`
- Migrasi otomatis saat startup:
  - `printform.ini` dan `printform.auth.json`: dari folder executable lama ke `%LocalAppData%`.
  - `printform.client-id`: prioritas dari `%LocalAppData%` lama, fallback dari folder executable lama, lalu disalin ke `%ProgramData%`.

build & run

```powershell
dotnet build .\PrintForm\PrintForm.csproj -c Release
```

```powershell
dotnet run --project .\PrintForm\PrintForm.csproj
```

release publish (target for installer)

```powershell
dotnet publish .\PrintForm\PrintForm.csproj -c Release -f net8.0-windows -o .\artifacts\publish
```

build installer (inno setup)

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\PrintForm.iss
```

installer output

```text
.\artifacts\installer\PrintForm-Setup-1.0.4.exe
```
