# Build Release PrintOrder Client

Dokumen ini berisi catatan internal untuk membuat installer release aplikasi PrintOrder Client.

Dokumen ini tidak ditujukan sebagai panduan publik, melainkan sebagai referensi build internal agar proses pembuatan installer dapat dilakukan secara konsisten.

## Ringkasan

PrintOrder Client adalah aplikasi desktop Windows yang digunakan oleh mitra percetakan untuk menerima dan memproses tugas cetak dari server PrintOrder.

Hasil akhir proses build adalah file installer Windows:

```txt id="vh5ukb"
PrintOrder-Setup-<versi>.exe
```

Contoh:

```txt id="wl70ln"
PrintOrder-Setup-1.4.2.exe
```

## Kebutuhan Build

Pastikan perangkat build menggunakan Windows dan sudah memiliki:

| Komponen             | Keterangan                                                         |
| -------------------- | ------------------------------------------------------------------ |
| .NET SDK             | Digunakan untuk melakukan publish aplikasi                         |
| Inno Setup 6         | Digunakan untuk membuat installer `.exe`                           |
| Git                  | Digunakan untuk mengambil source code dari repository              |
| SumatraPDF Installer | Opsional, digunakan jika ingin membundel SumatraPDF pada installer |

## Struktur File Terkait Release

```txt id="uwnbkt"
PrintForm/
├── PrintOrder/
│   ├── PrintOrder.csproj
│   └── Assets/
├── installer/
│   └── SumatraPDF-Installer.exe
├── artifacts/
│   ├── publish/
│   └── installer/
├── PrintOrder.iss
└── README.md
```

Keterangan:

| Path                                 | Keterangan                     |
| ------------------------------------ | ------------------------------ |
| `PrintOrder/PrintOrder.csproj`       | Project utama aplikasi desktop |
| `PrintOrder.iss`                     | Script installer Inno Setup    |
| `installer/SumatraPDF-Installer.exe` | Installer SumatraPDF opsional  |
| `artifacts/publish/`                 | Output hasil publish aplikasi  |
| `artifacts/installer/`               | Output installer final         |

## Versi Aplikasi

Sebelum build release, pastikan versi aplikasi sudah sesuai pada dua lokasi berikut:

1. `PrintOrder/PrintOrder.csproj`
2. `PrintOrder.iss`

Pada `PrintOrder.csproj`, sesuaikan bagian:

```xml id="zmyuj5"
<Version>1.4.2</Version>
<FileVersion>1.4.2.0</FileVersion>
<AssemblyVersion>1.4.2.0</AssemblyVersion>
<InformationalVersion>1.4.2</InformationalVersion>
```

Pada `PrintOrder.iss`, sesuaikan bagian:

```pascal id="6oveb6"
#define MyAppVersion "1.4.2"
```

Pastikan versi pada project dan installer sama agar nama file installer, metadata aplikasi, dan informasi versi tidak berbeda.

## Membersihkan Output Lama

Sebelum melakukan build baru, disarankan menghapus output lama:

```powershell id="7s3fsq"
Remove-Item -Recurse -Force .\artifacts\publish -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force .\artifacts\installer -ErrorAction SilentlyContinue
```

Atau hapus manual folder berikut:

```txt id="v9i1eh"
artifacts\publish
artifacts\installer
```

## Publish Aplikasi

Jalankan perintah berikut dari root repository:

```powershell id="d3uhzf"
dotnet publish .\PrintOrder\PrintOrder.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\artifacts\publish
```

Keterangan:

| Opsi                      | Keterangan                                         |
| ------------------------- | -------------------------------------------------- |
| `-c Release`              | Menggunakan konfigurasi release                    |
| `-r win-x64`              | Target runtime Windows 64-bit                      |
| `--self-contained true`   | Aplikasi membawa runtime yang dibutuhkan           |
| `PublishSingleFile=false` | Output tidak digabung menjadi satu file            |
| `DebugType=None`          | Tidak menyertakan debug type                       |
| `DebugSymbols=false`      | Tidak menyertakan debug symbol                     |
| `-o .\artifacts\publish`  | Output publish masuk ke folder `artifacts\publish` |

Setelah publish selesai, pastikan file berikut tersedia:

```txt id="80tiom"
artifacts\publish\PrintOrder.exe
```

## Bundling SumatraPDF

SumatraPDF digunakan sebagai pendukung proses cetak PDF, terutama untuk kebutuhan cetak PDF dengan rentang halaman tertentu.

Jika ingin installer PrintOrder ikut menawarkan instalasi SumatraPDF, letakkan installer resmi SumatraPDF pada path berikut:

```txt id="vy5e3f"
installer\SumatraPDF-Installer.exe
```

Jika file tersebut tersedia, script installer akan membundel SumatraPDF sebagai prerequisite.

Jika file tersebut tidak tersedia, installer PrintOrder tetap dapat dibuat, tetapi saat setup dijalankan akan menampilkan informasi bahwa SumatraPDF belum terdeteksi dan pengguna dapat menginstalnya secara terpisah.

## Build Installer

Setelah proses publish selesai, jalankan Inno Setup dari root repository:

```powershell id="nk1s9t"
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\PrintOrder.iss
```

Jika Inno Setup dipasang pada lokasi berbeda, sesuaikan path `ISCC.exe`.

Output installer akan dibuat pada:

```txt id="tlg1ef"
artifacts\installer\
```

Contoh output:

```txt id="rh429p"
artifacts\installer\PrintOrder-Setup-1.4.2.exe
```

## Checklist Sebelum Release

Sebelum installer dibagikan, lakukan pengecekan berikut:

| Pemeriksaan                                                    | Status |
| -------------------------------------------------------------- | ------ |
| Versi di `PrintOrder.csproj` sudah benar                       |        |
| Versi di `PrintOrder.iss` sudah benar                          |        |
| Folder `artifacts\publish` sudah berisi hasil publish terbaru  |        |
| Installer berhasil dibuat di `artifacts\installer`             |        |
| Installer dapat dijalankan di Windows                          |        |
| Aplikasi dapat dibuka setelah instalasi                        |        |
| Logo dan nama aplikasi tampil dengan benar                     |        |
| Base URL default sesuai server produksi                        |        |
| Aplikasi dapat terhubung ke server                             |        |
| Pair akun mitra berhasil                                       |        |
| Daftar printer lokal terbaca                                   |        |
| Daftar tugas cetak dapat dibuka                                |        |
| Notifikasi tugas baru berjalan                                 |        |
| Cetak PDF berhasil                                             |        |
| Cetak dengan rentang halaman berhasil jika SumatraPDF tersedia |        |
| Aplikasi tetap berjalan di system tray saat dashboard ditutup  |        |
| Auto-start Windows berjalan jika diaktifkan                    |        |

## Pengujian Installer

Setelah installer dibuat, lakukan pengujian pada perangkat Windows yang bersih atau perangkat uji.

Langkah pengujian:

1. Jalankan installer `PrintOrder-Setup-<versi>.exe`.
2. Selesaikan proses instalasi.
3. Buka aplikasi PrintOrder.
4. Periksa apakah aplikasi tampil normal.
5. Buka pengaturan dan pastikan Base URL sesuai.
6. Hubungkan aplikasi ke akun mitra.
7. Pastikan client muncul pada portal mitra.
8. Pastikan printer lokal terbaca.
9. Kirim tugas cetak dari halaman pelanggan.
10. Pastikan tugas cetak masuk ke aplikasi client.
11. Coba proses cetak.
12. Pastikan status tugas cetak berubah sesuai hasil proses.
13. Tutup dashboard dan pastikan aplikasi tetap aktif di system tray.
14. Keluar dari aplikasi melalui system tray jika pengujian selesai.

## Penamaan Installer

Gunakan format nama berikut:

```txt id="tv76cg"
PrintOrder-Setup-<versi>.exe
```

Contoh:

```txt id="rxvgrk"
PrintOrder-Setup-1.4.2.exe
```

Jangan mengubah nama installer secara manual jika tidak diperlukan. Nama installer sebaiknya mengikuti versi yang ditentukan pada `PrintOrder.iss`.

## Catatan Distribusi Internal

Installer release dapat digunakan untuk kebutuhan:

* Pengujian sistem.
* Demo aplikasi.
* Dokumentasi tugas akhir.
* Deployment pada perangkat mitra percetakan.
* Pengarsipan versi final.

Karena aplikasi ini bukan proyek open source, installer dan source code tidak ditujukan untuk distribusi bebas.

## Troubleshooting

### `dotnet` tidak dikenali

Pastikan .NET SDK sudah terinstal dan tersedia di PATH.

Cek dengan:

```powershell id="uvj8tj"
dotnet --version
```

### Build gagal karena dependency

Jalankan restore terlebih dahulu:

```powershell id="uhghpn"
dotnet restore .\PrintOrder\PrintOrder.csproj
```

Lalu ulangi publish.

### Inno Setup tidak ditemukan

Pastikan Inno Setup 6 sudah terinstal.

Lokasi umum:

```txt id="u2foh0"
C:\Program Files (x86)\Inno Setup 6\ISCC.exe
```

Jika berbeda, sesuaikan path pada perintah build installer.

### Installer berhasil dibuat tetapi aplikasi tidak berjalan

Cek hal berikut:

1. Pastikan file `PrintOrder.exe` ada di folder hasil instalasi.
2. Pastikan publish dilakukan dalam konfigurasi `Release`.
3. Pastikan target runtime sesuai perangkat pengguna.
4. Coba jalankan aplikasi langsung dari `artifacts\publish`.
5. Periksa apakah ada file pendukung yang tidak ikut masuk ke publish folder.

### Cetak PDF tidak berjalan

Cek apakah SumatraPDF tersedia pada perangkat.

Jika belum tersedia, instal SumatraPDF secara manual atau bundel installer SumatraPDF ke:

```txt id="jajuku"
installer\SumatraPDF-Installer.exe
```

Lalu build ulang installer PrintOrder.

## Ringkasan Perintah Build

Dari root repository:

```powershell id="7h7b2o"
Remove-Item -Recurse -Force .\artifacts\publish -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force .\artifacts\installer -ErrorAction SilentlyContinue

dotnet publish .\PrintOrder\PrintOrder.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\artifacts\publish

& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\PrintOrder.iss
```

Output akhir:

```txt id="fp9wdw"
artifacts\installer\PrintOrder-Setup-1.4.2.exe
```

## Catatan

Simpan installer release final pada lokasi yang aman. Jika installer digunakan sebagai lampiran atau bukti implementasi, catat versi, tanggal build, branch, dan commit yang digunakan.
