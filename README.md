# PrintForm

## Pairing model (desktop client)

- Tombol utama di desktop sekarang memakai mode **Pair Akun / Lepas Pairing** (bukan login/logout harian).
- Pairing dilakukan sekali dengan `identifier` + `password` akun mitra lewat endpoint `POST /api/clients/:id/pair`.
- Setelah itu app tidak perlu minta password lagi di setiap startup selama refresh token masih valid.
- Lepas pairing akan memanggil `POST /api/clients/:id/unbind`.
- Lepas pairing dari desktop mewajibkan verifikasi PIN akun (atur PIN di `/mitra/account/`).

## Lokasi file lokal

- Konfigurasi dan state auth sekarang disimpan di:
  - `%LocalAppData%\\PrintForm\\printform.ini`
  - `%LocalAppData%\\PrintForm\\printform.client-id`
  - `%LocalAppData%\\PrintForm\\printform.auth.json`
- Jika file lama masih berada di folder executable, app akan mencoba migrasi otomatis saat startup.

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
