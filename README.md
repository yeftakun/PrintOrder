# PrintOrder

PrintOrder adalah aplikasi desktop Windows untuk menerima dan mencetak tugas cetak dari server PrintOrder melalui printer lokal.

## Fitur Utama

- Pair akun client ke server PrintOrder.
- Pilih printer aktif dari daftar printer Windows.
- Terima tugas cetak secara realtime.
- Cetak, retry, atau tolak tugas cetak.
- Berjalan di system tray setelah dashboard ditutup.
- Pengaturan Base URL, notifikasi, auto start Windows, dan status SumatraPDF.

## Kebutuhan

- Windows 10/11.
- Printer lokal sudah terpasang.
- SumatraPDF direkomendasikan, dan diperlukan untuk cetak PDF.

## Instalasi

Gunakan installer release:

```text
PrintOrder-Setup-1.3.0.exe
```

Setelah instalasi:

1. Buka PrintOrder.
2. Masuk ke `Pengaturan` dan pastikan `Base URL` sesuai alamat server.
3. Klik `Pair Akun`.
4. Pilih printer aktif.
5. Buka `Daftar Tugas Cetak` untuk memantau dan memproses job.

## Lokasi Data

- Konfigurasi: `%LocalAppData%\PrintOrder\printorder.ini`
- Sesi pairing: `%LocalAppData%\PrintOrder\printorder.auth.json`
- Client ID: `%ProgramData%\PrintOrder\printorder.client-id`

## Build Release

Publish self-contained agar pengguna tidak perlu install .NET runtime:

```powershell
dotnet publish .\PrintOrder\PrintOrder.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\artifacts\publish
```

Untuk membundel SumatraPDF sebagai prerequisite, letakkan installer resmi di:

```text
installer\SumatraPDF-Installer.exe
```

Lalu build installer:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\PrintOrder.iss
```

Output installer:

```text
artifacts\installer\PrintOrder-Setup-1.3.0.exe
```
