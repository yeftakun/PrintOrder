# THISSYS - Penjelasan Sistem PrintOrder Desktop

Dokumen ini menjelaskan keseluruhan aplikasi desktop PrintOrder dalam bentuk narasi alur. Formatnya mengikuti cara berpikir sequence diagram: siapa aktornya, modul apa yang menerima aksi, urutan pesan yang terjadi, perubahan state yang muncul, dan hasil akhirnya. Dokumen ini tidak memakai diagram visual supaya tetap mudah dibaca dan mudah diubah saat sistem berkembang.

## Gambaran Umum

PrintOrder adalah aplikasi desktop Windows berbasis .NET 8 WinForms. Perannya adalah menjadi client lokal yang terhubung ke server PrintOrder, menerima job cetak dari server, lalu mengirim file job ke printer yang terpasang di komputer pengguna.

Aplikasi ini punya beberapa tanggung jawab utama:

- Menyimpan identitas client lokal dalam bentuk GUID.
- Menghubungkan client ke akun mitra melalui proses pairing.
- Menjaga sesi login/pairing memakai access token dan refresh token.
- Mendaftarkan client dan printer lokal ke server.
- Mengirim heartbeat agar server tahu client masih online.
- Menerima sinyal realtime dari WebSocket dan polling fallback.
- Menampilkan daftar job cetak yang aktif.
- Memproses job cetak ke printer lokal.
- Mengubah status job di server sesuai hasil proses lokal.
- Menyediakan pengaturan base URL, notifikasi, auto start, dan status SumatraPDF.
- Tetap berjalan di system tray saat dashboard ditutup.

Secara arsitektur, aplikasi ini bukan aplikasi server. Aplikasi ini adalah client desktop yang bergantung pada API server PrintOrder untuk autentikasi, job, status, dan event realtime. Operasi cetak tetap terjadi di mesin Windows lokal.

## Stack dan Batas Sistem

Stack utama:

- Bahasa: C#.
- Framework: .NET 8, target `net8.0-windows`.
- UI: Windows Forms.
- Printing: `System.Drawing.Printing.PrintDocument`, Shell print verb, SumatraPDF, Microsoft Edge.
- Printer status: `System.Management` untuk query WMI `Win32_Printer`.
- Komunikasi server: `HttpClient` dan `ClientWebSocket`.
- Persistensi lokal: file INI, JSON, file GUID, registry Windows untuk auto start.
- Installer: Inno Setup.

Batas sistem:

- Aplikasi tidak menyimpan database lokal.
- Aplikasi tidak menyediakan API sendiri.
- Aplikasi tidak memutus presence server saat close; server diasumsikan menentukan offline dari heartbeat timeout.
- Aplikasi memproses satu job cetak pada satu waktu melalui flag `_jobProcessing`.
- Server tetap menjadi sumber kebenaran untuk status job dan pairing.

## Peta Modul

Modul utama dan perannya:

- `Program.cs`: entry point, single instance, pemeriksaan file wajib, relaunch administrator jika perlu.
- `AppConfig.cs`: baca/tulis konfigurasi, client ID, auth state, migrasi file lama.
- `Form1.cs`: dashboard utama, orkestrator server, auth, heartbeat, realtime, tray, dan proses printing.
- `LoginForm.cs`: dialog pairing akun dan dialog PIN untuk unpair.
- `JobListForm.cs`: daftar job, pencarian, filter, detail job, delegasi aksi print/reject ke `Form1`.
- `PrintJobModels.cs`: model data job dan konfigurasi print.
- `SettingsForm.cs`: layar pengaturan base URL, notifikasi, SumatraPDF, auto start.
- `SumatraPdfSupport.cs`: deteksi executable SumatraPDF dan buka halaman download.
- `ToastNotificationForm.cs`: toast desktop internal aplikasi.
- `DashboardControls.cs`: theme, drawing helper, rounded panel/button, icon painter.
- `AboutForm.cs`: informasi aplikasi, client ID, versi, dan lokasi konfigurasi.
- `PrintOrder.iss`: script installer Inno Setup.

## Data Lokal

Data lokal disimpan di lokasi berikut:

- Konfigurasi user: `%LocalAppData%\PrintOrder\printorder.ini`.
- Auth pairing: `%LocalAppData%\PrintOrder\printorder.auth.json`.
- Client ID mesin: `%ProgramData%\PrintOrder\printorder.client-id`.

Isi data:

- `printorder.ini` menyimpan `base_url`, `sound_enabled`, dan `notification_enabled`.
- `printorder.auth.json` menyimpan `RefreshToken`, `UserId`, dan `Username`.
- `printorder.client-id` menyimpan GUID stabil agar server mengenali client yang sama antar restart dan antar user Windows.

`AppConfig` juga punya logika migrasi dari nama/lokasi lama `PrintForm`, sehingga instalasi atau versi lama masih bisa dibaca.

## Alur 1 - Startup Aplikasi

Pemicu:

- User menjalankan `PrintOrder.exe`.

Aktor/modul:

- User.
- `Program`.
- `AppConfig`.
- Windows.
- `Form1`.

Urutan:

1. `Program.Main` memeriksa apakah proses dijalankan dengan command khusus, misalnya `--save-server-base-url`.
2. Jika tidak ada command khusus, aplikasi menginisialisasi WinForms lewat `ApplicationConfiguration.Initialize()`.
3. `Program` meminta `AppConfig.GetMissingRequiredFiles()` untuk memastikan file wajib tersedia.
4. `AppConfig` mencoba migrasi file lama dulu, lalu mengecek `printorder.ini` dan `printorder.client-id`.
5. Jika ada file wajib yang hilang, aplikasi menampilkan konfirmasi ke user.
6. Jika user setuju, `AppConfig.CreateMissingRequiredFiles()` membuat config default dan client ID baru.
7. Jika file tidak bisa dibuat karena izin, `Program` mencoba relaunch aplikasi sebagai administrator dengan argumen `--create-required-files`.
8. Setelah file wajib siap, `Program` membuat mutex `Local\PrintOrder.SingleInstance`.
9. Jika aplikasi sudah berjalan, user diberi pesan bahwa PrintOrder sudah aktif.
10. Jika ini instance pertama, aplikasi menjalankan `Application.Run(new Form1())`.

Hasil:

- Hanya satu instance aplikasi aktif.
- Konfigurasi dasar dan client ID siap sebelum dashboard berjalan.
- Dashboard utama `Form1` menjadi pusat operasi.

Catatan:

- `base_url` default adalah `https://printorder.web.id`.
- Jika client ID lama tidak valid sebagai GUID, `AppConfig` membuat GUID baru.

## Alur 2 - Inisialisasi Dashboard Utama

Pemicu:

- `Form1` selesai dibuat dan event load berjalan.

Aktor/modul:

- `Form1`.
- Windows printer subsystem.
- `AppConfig`.
- Server PrintOrder.
- Timer WinForms.
- WebSocket server.

Urutan:

1. `Form1_Load` mengosongkan dan mengisi `comboPrinters` dari `PrinterSettings.InstalledPrinters`.
2. Jika printer tersedia, printer default Windows dipilih.
3. `Form1` menampilkan base URL yang dibaca dari `AppConfig.LoadServerBaseUrl()`.
4. `Form1` memanggil `LoadPersistedAuthState()` untuk membaca refresh token dan identitas user dari file auth lokal.
5. `Form1` memperbarui label client ID dan status dashboard.
6. Jika refresh token ada, `Form1` mencoba memulihkan access token lewat `TryRefreshAccessTokenAsync(false)`.
7. `Form1` memanggil `EnsureRegisteredAsync()`.
8. `EnsureRegisteredAsync()` memastikan client ID tersedia, lalu memanggil `RegisterClientAsync()`.
9. `RegisterClientAsync()` mengirim `clientId`, nama mesin, daftar printer, dan printer terpilih ke endpoint register server.
10. Jika server mengembalikan client ID baru/valid, nilai `_clientId` diperbarui.
11. Setelah register, `Form1` menjalankan tiga mekanisme sinkronisasi:
    - `StartHeartbeat()` setiap 30 detik.
    - `StartPingPolling()` setiap 5 detik.
    - `StartRealtime()` untuk WebSocket.

Hasil:

- Dashboard mengetahui printer lokal.
- Client terdaftar atau setidaknya dicoba didaftarkan ke server.
- Sesi pairing dipulihkan jika refresh token masih valid.
- Sinkronisasi server mulai berjalan.

## Alur 3 - Konfigurasi Lokal dan Validasi Base URL

Pemicu:

- Startup aplikasi.
- User membuka Settings dan menyimpan perubahan.
- Command line `--save-server-base-url`.

Aktor/modul:

- `AppConfig`.
- `SettingsForm`.
- `Program`.
- File system Windows.

Urutan baca konfigurasi:

1. Modul yang membutuhkan konfigurasi memanggil `AppConfig.LoadServerBaseUrl()`.
2. `AppConfig` mencoba migrasi file lama.
3. File `printorder.ini` dibaca baris per baris.
4. Baris kosong, komentar, dan header section dilewati.
5. Key `base_url` dicari.
6. Value di-trim, tanda kutip dihapus, dan trailing slash dibuang.
7. URL diterima hanya jika `Uri.TryCreate` berhasil dan scheme adalah `http` atau `https`.
8. Jika invalid atau gagal baca, default server dipakai.

Urutan simpan konfigurasi:

1. `SettingsForm` mengambil input dari textbox.
2. Input dinormalisasi dengan `Trim()`, `Trim('"')`, dan `TrimEnd('/')`.
3. `AppConfig.IsValidServerBaseUrl()` dipakai untuk validasi.
4. Jika valid, `AppConfig.SaveAppSettings()` menulis ulang `printorder.ini`.
5. Opsi notifikasi disimpan bersama base URL.

Hasil:

- Aplikasi selalu punya base URL yang usable.
- Input base URL yang kosong atau bukan HTTP/HTTPS ditolak.
- Perubahan base URL disimpan ke file konfigurasi user.

Catatan:

- Jika base URL berubah, `Form1` menampilkan pesan bahwa aplikasi perlu dibuka ulang agar koneksi baru dipakai penuh.

## Alur 4 - Pairing Akun

Pemicu:

- User menekan tombol `Pair Akun` saat belum terhubung.

Aktor/modul:

- User.
- `LoginForm`.
- `Form1`.
- `AppConfig`.
- Server PrintOrder.

Urutan:

1. User menekan tombol login/pairing di dashboard.
2. `Form1.btnLogin_Click` melihat bahwa belum ada refresh token lokal.
3. `Form1` membuka `LoginForm`.
4. `LoginForm` meminta username/email dan password.
5. `LoginForm.Submit()` memastikan identifier dan password tidak kosong.
6. Jika valid, dialog ditutup dengan `DialogResult.OK`.
7. `Form1.LoginAsync(identifier, password)` dipanggil.
8. `LoginAsync` memastikan client sudah register lewat `EnsureRegisteredAsync()`.
9. `LoginAsync` mengirim JSON `{ identifier, password }` ke endpoint:
   - `POST /api/clients/{clientId}/pair`
10. Jika server menolak, status dashboard menampilkan error sesuai HTTP status atau field `error`.
11. Jika sukses, `TryApplyAuthBundle()` membaca `accessToken`, `refreshToken`, dan optional `user`.
12. Access token disimpan di memory.
13. Refresh token, user id, dan username disimpan ke `printorder.auth.json` lewat `PersistAuthState()`.
14. UI auth diperbarui menjadi mode terhubung.
15. `SendHeartbeatAsync()` dipanggil agar server segera menerima status client yang sudah paired.

Hasil:

- Client desktop terhubung ke akun mitra.
- Tombol job list aktif.
- Refresh token tersimpan lokal untuk pemulihan sesi saat aplikasi dibuka ulang.

Catatan:

- Password tidak disimpan lokal.
- Identifier di-trim, password tidak di-trim oleh property `Password` agar nilai password asli tetap dikirim.

## Alur 5 - Refresh Token dan Request Terotorisasi

Pemicu:

- Startup dengan refresh token lama.
- Request API membutuhkan access token.
- Server mengembalikan HTTP 401.

Aktor/modul:

- `Form1`.
- `HttpClient`.
- Server PrintOrder.
- `AppConfig`.

Urutan refresh eksplisit:

1. `TryRefreshAccessTokenAsync()` membaca `_refreshToken`.
2. Semaphore `_authRefreshLock` mencegah refresh paralel.
3. Refresh token dikirim ke endpoint refresh auth.
4. Jika server mengembalikan 401, state auth lokal dihapus.
5. Jika sukses, `TryApplyAuthBundle()` memperbarui access token dan refresh token.
6. Auth state baru ditulis ulang ke file JSON.

Urutan request terotorisasi:

1. Modul membuat `HttpRequestMessage`.
2. Modul memanggil `SendAuthorizedAsync(request)`.
3. Jika access token kosong tetapi refresh token ada, aplikasi mencoba refresh dulu.
4. Request di-clone melalui `CloneRequestAsync()` supaya bisa dicoba ulang jika 401.
5. Header `Authorization: Bearer <accessToken>` dipasang.
6. Request dikirim lewat shared `HttpClient`.
7. Jika response bukan 401, response dikembalikan.
8. Jika response 401 dan refresh token masih ada, aplikasi refresh token.
9. Jika refresh berhasil, request clone dikirim ulang dengan token baru.

Hasil:

- Modul lain tidak perlu mengatur token manual.
- Access token bisa pulih otomatis selama refresh token masih valid.
- Jika refresh gagal karena unauthorized, pairing lokal dianggap habis dan dibersihkan.

## Alur 6 - Register Client, Heartbeat, dan Ping Polling

Pemicu:

- Startup dashboard.
- Timer heartbeat setiap 30 detik.
- Timer ping polling setiap 5 detik.
- Perubahan printer terpilih.

Aktor/modul:

- `Form1`.
- Windows printer subsystem.
- Server PrintOrder.

Urutan register:

1. `EnsureRegisteredAsync()` memastikan `_clientId` tersedia.
2. `RegisterClientAsync()` mengambil daftar printer lokal.
3. Payload register berisi:
   - `clientId`
   - `name` dari `Environment.MachineName`
   - `printers`
   - `selectedPrinter`
4. Payload dikirim ke:
   - `POST /api/clients/register`
5. Jika server mengembalikan field `id` berupa GUID, `_clientId` dinormalisasi.
6. Jika server mengembalikan `recognized=false` sementara auth lokal ada, auth lokal dibersihkan.
7. Status dashboard diperbarui.

Urutan heartbeat:

1. Timer memanggil `SendHeartbeatAsync()`.
2. Aplikasi memastikan client terdaftar.
3. Payload heartbeat berisi `clientId` dan `selectedPrinter`.
4. Payload dikirim ke:
   - `POST /api/clients/heartbeat`
5. Jika server mengembalikan 404, aplikasi register ulang.
6. Jika server mengembalikan 401, dashboard meminta pairing.
7. Jika response sukses tetapi `recognized=false`, auth lokal dihapus.

Urutan ping polling:

1. Timer memanggil `PollPingAsync()`.
2. Aplikasi memastikan client terdaftar.
3. Request dikirim ke:
   - `GET /api/clients/{clientId}/ping`
4. Jika ada item ping, status dashboard dan message box memberi tahu user.
5. Jika server tidak bisa dihubungi, error polling diabaikan agar aplikasi tetap berjalan.

Hasil:

- Server bisa melihat client, printer, dan status online.
- Jika client hilang di server, aplikasi mencoba register ulang.
- Polling menjadi fallback komunikasi selain WebSocket.

## Alur 7 - Realtime WebSocket

Pemicu:

- `Form1.StartRealtime()` dipanggil saat startup dashboard.

Aktor/modul:

- `Form1`.
- `ClientWebSocket`.
- Server WebSocket PrintOrder.
- `JobListForm`.
- `ToastNotificationForm`.
- `SoundPlayer`.

Urutan koneksi:

1. `StartRealtime()` membuat `CancellationTokenSource`.
2. Worker background menjalankan `RunRealtimeLoopAsync()`.
3. `BuildRealtimeUri()` mengubah base URL:
   - `https` menjadi `wss`
   - `http` menjadi `ws`
   - path menjadi `/ws`
   - query berisi `clientId` dan `role=client`
4. WebSocket connect ke URI tersebut.
5. Setelah connect, client mengirim payload identify:
   - `action=identify`
   - `clientId`
   - `role=client`
6. Client mengirim payload subscribe:
   - `action=subscribe`
   - `channels=["jobs","clients","sessions"]`
7. `ReceiveRealtimeMessagesAsync()` membaca message text sampai socket close atau cancel.

Urutan event:

1. Message JSON masuk ke `HandleRealtimeMessage(payload)`.
2. Field `type` dibaca.
3. Untuk `job.created`, aplikasi mengecek apakah event relevan untuk client saat ini.
4. Jika relevan, aplikasi menjalankan notifikasi job masuk dan meminta refresh job list.
5. Untuk `job.status.changed` atau `jobs.removed`, aplikasi meminta refresh job list jika event relevan.
6. Jika parsing gagal atau type tidak dikenal, message diabaikan.

Urutan notifikasi:

1. `NotifyIncomingJobFromRealtime()` hanya berjalan jika auth lokal ada.
2. Jika opsi sound dan desktop notification sama-sama mati, tidak ada notifikasi.
3. Job ID dibaca dari payload.
4. HashSet `_notifiedRealtimeJobIds` mencegah notifikasi ganda untuk job yang sama.
5. Pesan notifikasi disusun dari `originalName`, `alias`, atau `id`.
6. Jika desktop notification aktif, `ToastNotificationForm.ShowNotification()` dipanggil di UI thread.
7. Jika sound aktif, file `Assets\sounds\job_incoming.wav` dimainkan.
8. Jika `JobListForm` sedang terbuka, `RequestRefreshFromRealtime()` dipanggil.

Hasil:

- Daftar job bisa refresh cepat tanpa menunggu polling manual.
- User mendapat toast dan/atau suara saat job baru masuk.
- Jika WebSocket gagal, loop mencoba reconnect setelah delay 3 detik, sementara polling tetap berjalan.

## Alur 8 - Membuka Daftar Job

Pemicu:

- User menekan tombol `Daftar Tugas Cetak`.

Aktor/modul:

- User.
- `Form1`.
- `JobListForm`.
- Server PrintOrder.

Urutan:

1. `Form1.btnJobList_Click` memeriksa apakah refresh token lokal ada.
2. Jika belum paired, dashboard menampilkan pesan agar user pair akun dulu.
3. Jika sudah paired, `Form1` membuat `JobListForm` jika belum ada atau sudah disposed.
4. `Form1` menyuntikkan dependensi ke `JobListForm`:
   - base URL server.
   - function untuk mengambil client ID.
   - function `SendAuthorizedAsync`.
   - function `PrintJobFromListAsync`.
   - function `RejectJobFromListAsync`.
5. `JobListForm` ditampilkan dan dibawa ke depan.
6. Saat `JobListForm.Shown`, `LoadJobsAsync()` dipanggil.
7. Timer internal `JobListForm` juga memanggil `LoadJobsAsync()` setiap 5 detik.

Hasil:

- `JobListForm` tidak memiliki logika token sendiri.
- `Form1` tetap menjadi pemilik autentikasi dan operasi print.
- `JobListForm` fokus pada tampilan, filter, refresh, dan delegasi aksi.

## Alur 9 - Sinkronisasi Daftar Job

Pemicu:

- Form job list pertama tampil.
- Timer refresh job list.
- Tombol refresh.
- Event realtime meminta refresh.
- Setelah print/reject selesai.

Aktor/modul:

- `JobListForm`.
- `Form1.SendAuthorizedAsync`.
- Server PrintOrder.

Urutan:

1. `LoadJobsAsync()` menolak refresh paralel dengan flag `_isLoading`.
2. Jika refresh sedang berjalan, `_pendingRefresh=true` supaya refresh diulang setelah request selesai.
3. Client ID diambil dari callback `_getClientId`.
4. Request dikirim ke:
   - `GET /api/jobs?claimClientId={clientId}&activeSessionOnly=true`
5. Jika response 401, job lokal dikosongkan dan user diminta login.
6. Jika response gagal, status list menampilkan gagal memuat job.
7. Jika response sukses, body JSON dibaca.
8. `DeserializeVisibleJobs()` menerima dua format:
   - array job langsung.
   - object dengan field `items`.
9. `AddJobIfVisible()` menyaring job yang session status-nya `expired` atau `closed`.
10. Job yang lolos disimpan ke `_allJobs`.
11. Metric total/ready/pending/rejected/done diperbarui.
12. `RenderFilteredJobs()` menerapkan pencarian dan filter aktif.
13. Row UI dibuat ulang dari job hasil filter.

Hasil:

- Job list hanya menampilkan job sesi aktif.
- UI tetap responsif saat refresh karena tombol dan row action dinonaktifkan sementara.
- Jika refresh masuk saat refresh lama belum selesai, refresh susulan tidak hilang.

## Alur 10 - Pencarian, Filter, dan Detail Job

Pemicu:

- User mengetik di search box.
- User membuka dialog filter.
- User klik row job untuk detail.

Aktor/modul:

- User.
- `JobListForm`.
- `JobFilterDialog`.
- `JobDetailPage`.

Urutan pencarian:

1. Text search di-trim.
2. `MatchesSearch()` mengecek apakah query ada pada:
   - `OriginalName`
   - `Alias`
   - `PrintConfig.PaperSize`
   - label status
3. Hasil pencarian digabung dengan filter aktif.
4. Row job dirender ulang.

Urutan filter:

1. User membuka `JobFilterDialog`.
2. Dialog menyediakan filter:
   - status
   - tipe dokumen: PDF, gambar, lainnya
   - ukuran kertas
   - actionable only
3. Saat user menekan terapkan, dialog membuat `JobFilterState`.
4. `JobFilterState.Matches(job)` dipakai saat render.
5. Badge filter menampilkan jumlah filter aktif.

Urutan detail:

1. User klik row atau aksi detail.
2. `ShowJobDetail(job)` membuat `JobDetailPage`.
3. List page disembunyikan.
4. Detail page menampilkan:
   - informasi dokumen
   - alias
   - ID job
   - status
   - waktu masuk
   - ukuran kertas
   - copies
   - mode warna
   - orientasi
   - page range
   - scale
   - notes
5. Tombol detail menggunakan callback print/reject yang sama dengan list.
6. Setelah aksi selesai, detail kembali ke list dan list refresh.

Hasil:

- User bisa menyeleksi job dengan cepat.
- Detail job tidak menggandakan logika server.
- Aksi dari list dan detail mengikuti validasi yang sama.

## Alur 11 - Print Job

Pemicu:

- User menekan `Cetak` untuk job `ready`.
- User menekan `Retry` untuk job `pending`.

Aktor/modul:

- User.
- `JobListForm` atau `JobDetailPage`.
- `Form1`.
- Server PrintOrder.
- Windows printer subsystem.
- SumatraPDF atau Edge.

Urutan dari UI:

1. `JobListForm.HandlePrintActionAsync(job)` dipanggil.
2. Sebelum print, form mengambil versi terbaru job dari:
   - `GET /api/jobs/{jobId}`
3. Jika job hilang, list refresh dan aksi dibatalkan.
4. Jika status bukan `ready` atau `pending`, list refresh dan aksi dibatalkan.
5. Jika status masih bisa diproses, callback `_printJobAsync(job)` dipanggil.
6. Callback itu adalah `Form1.PrintJobFromListAsync(job)`.

Urutan di `Form1`:

1. `PrintJobFromListAsync()` memastikan tidak ada job lain yang sedang diproses.
2. `ProcessJobAsync(job)` menandai `_jobProcessing=true` dan menyimpan `_activeJobId`.
3. Aplikasi mengirim heartbeat asinkron.
4. Printer terpilih dicek dengan `IsPrinterOffline()`.
5. Jika printer tidak ada atau offline, status job diubah ke `pending`:
   - `PATCH /api/jobs/{jobId}` dengan `{ status: "pending" }`
6. Jika printer siap, status job diubah ke `printing`.
7. Jika server menolak perubahan status, proses dibatalkan. Khusus 402 `INSUFFICIENT_CREDIT`, user diberi pesan kredit tidak cukup.
8. File job diunduh dari:
   - `GET /api/jobs/{jobId}/download`
9. File disimpan ke temp path dengan nama berbasis `jobId` dan `OriginalName`.
10. Jika download gagal, status job diubah ke `failed`.
11. Jika file gambar (`jpg`, `jpeg`, `png`, `bmp`), file dibaca menjadi `Bitmap`.
12. `ApplyPrintConfig(job)` menerapkan printer, margin, copies, color mode, orientation, paper size, dan F4 fallback ke Legal.
13. Jika job adalah gambar, `printDocument1.Print()` dipanggil.
14. Jika job bukan gambar, `PrintNonImageAsync()` memilih jalur print non-image.

Urutan print gambar:

1. `printDocument1.PrintPage` menerima canvas print.
2. Gambar di-fit ke halaman berdasarkan rasio gambar dan rasio kertas.
3. `_activeContentScale` diterapkan sebagai skala.
4. Gambar digambar center di halaman.
5. `EndPrint` menghapus file temp.
6. `EndPrint` mengubah status job ke `done` jika print action adalah `PrintToPrinter`, selain itu `failed`.

Urutan print PDF:

1. `PrintNonImageAsync()` melihat extension `.pdf`.
2. `TryNormalizePdfPageRange()` memvalidasi page range.
3. Jika page range valid, aplikasi mencoba SumatraPDF lebih dulu.
4. `TryPrintPdfWithSumatraAsync()` mencari executable SumatraPDF.
5. Jika SumatraPDF ditemukan, argumen print disusun dengan `-print-settings`, `-print-to`, `-silent`, dan `-exit-on-print`.
6. Proses Sumatra ditunggu sampai 15 detik.
7. Jika Sumatra gagal dan page range tidak kosong, job gagal karena Edge fallback tidak mendukung page range.
8. Jika page range kosong, aplikasi mencoba Edge headless.
9. Jika Edge headless gagal, aplikasi mencoba Edge minimized dengan mode app.
10. Jika semua gagal, status dashboard meminta user install SumatraPDF.

Urutan print file non-image selain PDF:

1. Aplikasi membuat `ProcessStartInfo` dengan `FileName=filePath`.
2. Verb `printto` dipakai.
3. Nama printer dikirim sebagai argument.
4. Shell Windows menangani aplikasi default untuk file tersebut.
5. Proses ditunggu sampai 10 detik.
6. Fungsi menganggap print sukses jika proses berhasil dipanggil.

Urutan akhir:

1. Jika print non-image mengembalikan sukses, status job diubah ke `done`.
2. Jika gagal, status job diubah ke `failed`.
3. File temp dihapus.
4. `_jobProcessing=false` dan `_activeJobId` dibersihkan.
5. `JobListForm` refresh ulang daftar job.

Hasil:

- Server menerima transisi status job berdasarkan tindakan client lokal.
- File sementara tidak dipertahankan setelah proses selesai.
- Job gambar dicetak langsung melalui `PrintDocument`.
- Job PDF diprioritaskan melalui SumatraPDF.
- Edge menjadi fallback PDF full document.

## Alur 12 - Reject Job

Pemicu:

- User menekan `Hapus` atau reject untuk job `ready`.

Aktor/modul:

- User.
- `JobListForm` atau `JobDetailPage`.
- `Form1`.
- Server PrintOrder.

Urutan:

1. `JobListForm.HandleRejectActionAsync(job)` mengambil versi terbaru job dari server.
2. Jika job tidak ditemukan, list refresh dan aksi dibatalkan.
3. Jika status bukan `ready`, list refresh dan aksi dibatalkan.
4. Jika masih `ready`, callback `_rejectJobAsync(job)` dipanggil.
5. Callback itu adalah `Form1.RejectJobFromListAsync(job)`.
6. `RejectJobFromListAsync()` memastikan tidak ada job lain sedang diproses.
7. Status job diubah ke `rejected` melalui:
   - `PATCH /api/jobs/{jobId}` dengan `{ status: "rejected" }`
8. Status dashboard menampilkan sukses atau error.
9. Job list refresh ulang.

Hasil:

- Job tidak dikirim ke printer.
- Server menerima status `rejected`.
- UI lokal tersinkron ulang dengan server.

## Alur 13 - Unpair Client

Pemicu:

- User menekan tombol `Lepas Pairing` saat client sudah paired.

Aktor/modul:

- User.
- `UnpairPinDialog`.
- `Form1`.
- Server PrintOrder.
- `AppConfig`.

Urutan:

1. `btnLogin_Click` melihat refresh token lokal ada, sehingga memanggil `UnpairClientAsync()`.
2. Jika client ID kosong, auth lokal langsung dibersihkan.
3. Jika client ID ada, aplikasi membuka `UnpairPinDialog`.
4. Dialog meminta PIN.
5. `UnpairPinDialog.Submit()` memastikan PIN 4 sampai 8 digit angka.
6. `VerifyAccountPinAsync(pin)` mengirim PIN ke:
   - `POST /api/auth/verify-pin`
7. Jika PIN gagal, proses berhenti.
8. Jika PIN valid, aplikasi mengirim unbind:
   - `POST /api/clients/{clientId}/unbind`
9. Jika server mengembalikan conflict "already unbound", auth lokal dibersihkan karena server sudah lebih dulu unbind.
10. Jika unbind sukses, `ClearAuthState()` menghapus access token, refresh token, user ID, username, dan file auth lokal.
11. Heartbeat dikirim agar server mendapat state terbaru.
12. UI auth kembali ke mode belum terhubung.

Hasil:

- Pairing akun dilepas dari client.
- Token lokal dihapus.
- Job list dinonaktifkan sampai user pairing ulang.

## Alur 14 - Settings

Pemicu:

- User menekan tombol `Pengaturan`.

Aktor/modul:

- User.
- `Form1`.
- `SettingsForm`.
- `AppConfig`.
- `SumatraPdfSupport`.
- Windows Registry.

Urutan buka settings:

1. `Form1.btnSettings_Click` membuat `SettingsForm`.
2. Form menerima base URL saat ini dan clone `NotificationOptions`.
3. `SettingsForm` membaca status auto start dari `WindowsAutoStart.IsEnabled()`.
4. UI dibangun dalam beberapa section:
   - Server.
   - Notifikasi.
   - PDF Engine.
   - Sistem.
   - Footer save/cancel.
5. Section PDF langsung menjalankan `RefreshPdfEngineStatus()`.

Urutan test koneksi:

1. User menekan `Test Koneksi`.
2. Input base URL dinormalisasi.
3. URL divalidasi harus HTTP/HTTPS.
4. `HttpClient` sementara dibuat dengan timeout 5 detik.
5. Request `GET /api/health` dikirim.
6. Status sukses, gagal, atau timeout ditampilkan di label test.

Urutan simpan:

1. User menekan `Simpan`.
2. Base URL dinormalisasi dan divalidasi.
3. Opsi notifikasi dibaca dari checkbox.
4. Aplikasi menghitung perubahan:
   - base URL berubah
   - notifikasi berubah
   - auto start berubah
5. Jika tidak ada perubahan, dialog ditutup tanpa simpan.
6. Jika base URL atau notifikasi berubah, `AppConfig.SaveAppSettings()` menulis config.
7. Jika auto start berubah, `WindowsAutoStart.SetEnabled()` menulis atau menghapus value registry:
   - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
   - value `PrintOrder Client`
8. Jika berhasil, property `SavedBaseUrl`, `SavedNotificationOptions`, dan `SavedChanges` diisi.
9. `Form1` memperbarui `_notificationOptions`.
10. Jika base URL berubah, user diberi pesan untuk buka ulang aplikasi.

Hasil:

- User bisa mengubah server, notifikasi, dan auto start.
- Status SumatraPDF bisa dicek dari UI.
- Pengaturan disimpan di lokasi user tanpa database.

## Alur 15 - SumatraPDF Support

Pemicu:

- Settings dibuka.
- Print PDF dijalankan.
- User menekan download SumatraPDF.

Aktor/modul:

- `SumatraPdfSupport`.
- Windows Registry.
- File system.
- `Form1`.
- `SettingsForm`.

Urutan deteksi:

1. `SumatraPdfSupport.Detect()` mengambil daftar candidate path.
2. Candidate berasal dari:
   - Registry App Paths.
   - Registry Uninstall.
   - `Program Files`.
   - `Program Files (x86)`.
   - `%LocalAppData%`.
   - PATH environment variable.
3. Setiap candidate di-normalize dengan `Path.GetFullPath`.
4. Candidate pertama yang file-nya ada dikembalikan sebagai `SumatraPdfInstallation`.
5. Jika tidak ada, `ExecutablePath=null`.

Urutan penggunaan saat print:

1. `Form1.TryPrintPdfWithSumatraAsync()` memanggil `ResolveExecutablePath()`.
2. Jika executable tidak ditemukan, fungsi mengembalikan `false`.
3. Jika ditemukan, proses SumatraPDF dipanggil untuk print PDF.

Hasil:

- SumatraPDF dipakai otomatis jika tersedia.
- Fitur page range PDF bergantung pada SumatraPDF.
- Jika tidak tersedia, aplikasi masih mencoba fallback Edge untuk full PDF.

## Alur 16 - Toast dan Sound Notification

Pemicu:

- Event realtime `job.created` relevan untuk client ini.

Aktor/modul:

- `Form1`.
- `ToastNotificationForm`.
- `SoundPlayer`.
- User.

Urutan toast:

1. `Form1` menyusun pesan job baru.
2. Jika desktop notification aktif, `ShowIncomingJobToastOnUiThread()` dipanggil.
3. Fungsi memastikan operasi berjalan di UI thread.
4. `ToastNotificationForm.ShowNotification()` membuat window kecil tanpa taskbar.
5. Jika toast lama masih ada, toast lama ditutup.
6. Toast ditampilkan di kanan bawah layar kerja.
7. Timer menutup toast setelah durasi selesai.

Urutan sound:

1. Jika sound notification aktif, `PlayIncomingJobSoundOnUiThread()` dipanggil.
2. File sound dicari di `Assets\sounds\job_incoming.wav`.
3. `SoundPlayer` dibuat dan di-cache.
4. Sound dimainkan tanpa memblokir UI.

Hasil:

- User mendapat sinyal lokal saat job baru masuk.
- Notifikasi bisa dimatikan dari Settings.
- Job yang sama tidak memunculkan notifikasi berulang.

## Alur 17 - System Tray dan Lifecycle

Pemicu:

- Dashboard ditutup.
- User klik tray icon.
- User memilih menu tray.
- User memilih exit.

Aktor/modul:

- User.
- `Form1`.
- `NotifyIcon`.
- Windows.

Urutan inisialisasi tray:

1. Constructor `Form1` memanggil `InitializeApplicationIcon()`.
2. Icon dicari dari asset `logo_printorder.ico` atau executable.
3. `InitializeTrayIcon()` membuat `NotifyIcon`.
4. Context menu tray berisi aksi:
   - tampilkan dashboard
   - buka portal
   - keluar
5. Double click tray menampilkan dashboard.

Urutan close dashboard:

1. `Form1_FormClosing` menerima event close.
2. Jika `_allowApplicationExit=false`, close dibatalkan.
3. Dashboard disembunyikan ke tray.
4. Aplikasi tetap hidup, timer dan realtime tetap berjalan.

Urutan exit:

1. User memilih keluar dari tray.
2. Aplikasi meminta konfirmasi.
3. Jika user setuju, `_allowApplicationExit=true`.
4. Timer dihentikan.
5. Realtime dihentikan.
6. Tray icon dan resource lain dibersihkan.
7. Form ditutup.

Hasil:

- Aplikasi bisa berjalan di background.
- Sinkronisasi job tetap aktif saat window utama tidak terlihat.
- Exit eksplisit membersihkan timer, websocket, icon, sound player, dan image cache.

## Alur 18 - About dan Dashboard Controls

Pemicu:

- User membuka halaman info.
- UI dashboard dan form lain dirender.

Aktor/modul:

- `AboutForm`.
- `DashboardControls`.
- Asset icon/logo.
- User.

`AboutForm`:

1. Menerima client ID dan base URL dari `Form1`.
2. Menampilkan informasi aplikasi, versi, client ID, base URL, dan folder konfigurasi.
3. Menyediakan aksi copy client ID.
4. Menyediakan aksi buka folder konfigurasi.
5. Logo dibaca dari asset aplikasi.

`DashboardControls`:

1. Menyediakan warna theme melalui `UiTheme`.
2. Menyediakan helper rounded rectangle dan color resolving melalui `UiDrawing`.
3. Menyediakan icon loading/drawing dari PNG asset melalui `IconAssets`.
4. Menyediakan painter fallback untuk icon vektor.
5. Menyediakan komponen reusable seperti `RoundedPanel`, `RoundedButton`, dan `IconBadge`.

Hasil:

- UI punya gaya visual konsisten tanpa library UI eksternal.
- Form utama, settings, login, job list, dan about memakai komponen yang sama.

## Alur 19 - Installer dan Distribusi

Pemicu:

- Developer membuat release.

Aktor/modul:

- Developer.
- .NET publish.
- Inno Setup.
- `PrintOrder.iss`.
- Optional `installer\SumatraPDF-Installer.exe`.

Urutan build:

1. Developer menjalankan `dotnet publish` ke folder `artifacts\publish`.
2. `PrintOrder.iss` membaca output publish sebagai source installer.
3. File PDB dan `printorder.ini` dikecualikan dari installer.
4. Jika `installer\SumatraPDF-Installer.exe` ada, script mengaktifkan bundling prerequisite.
5. Installer mengecek apakah SumatraPDF sudah ada.
6. Jika belum ada dan installer Sumatra dibundel, task install SumatraPDF ditawarkan.
7. Installer membuat shortcut Start Menu dan optional desktop icon.
8. Setelah install, aplikasi bisa langsung diluncurkan.

Hasil:

- Aplikasi didistribusikan sebagai installer Windows.
- SumatraPDF bisa dibundel untuk mendukung PDF page range.
- Jika SumatraPDF tidak dibundel dan belum terdeteksi, installer memberi informasi bahwa fitur page range PDF memerlukan SumatraPDF.

## Model Data

`PrintJob`:

- `Id`: identitas job dari server.
- `OriginalName`: nama file asli.
- `Alias`: nama alternatif dari server/user.
- `Status`: status job.
- `CreatedAt`: waktu masuk.
- `PrintConfig`: konfigurasi cetak.

`PrintConfig`:

- `PaperSize`: ukuran kertas yang dicari pada printer lokal.
- `Copies`: jumlah salinan.
- `ColorMode`: `color` atau `bw`.
- `Orientation`: `portrait` atau `landscape`.
- `PageRange`: rentang halaman PDF.
- `PageRangeSnakeCase`: mapping JSON untuk `page_range`.
- `ContentScale`: skala konten.
- `Notes`: catatan job.

`AuthState`:

- `RefreshToken`: token untuk memperbarui access token.
- `UserId`: ID user mitra.
- `Username`: nama user mitra.

`NotificationOptions`:

- `SoundEnabled`: aktif/nonaktif suara.
- `DesktopEnabled`: aktif/nonaktif toast desktop.

## Kontrak API yang Terlihat dari Client

Endpoint yang dipakai aplikasi:

- `POST /api/clients/register`
- `POST /api/clients/heartbeat`
- `GET /api/clients/{clientId}/ping`
- `POST /api/clients/{clientId}/pair`
- `POST /api/clients/{clientId}/unbind`
- `POST /api/auth/verify-pin`
- `POST /api/auth/refresh`
- `GET /api/jobs?claimClientId={clientId}&activeSessionOnly=true`
- `GET /api/jobs/{jobId}`
- `GET /api/jobs/{jobId}/download`
- `PATCH /api/jobs/{jobId}`
- `GET /api/health`
- WebSocket `/ws`

Event WebSocket yang diproses:

- `job.created`
- `job.status.changed`
- `jobs.removed`

Channel WebSocket yang di-subscribe:

- `jobs`
- `clients`
- `sessions`

Status job yang dipakai client:

- `ready`: bisa dicetak.
- `pending`: tertunda, bisa retry.
- `printing`: sedang diproses client.
- `done`: sudah dikirim ke printer/spooler.
- `failed`: gagal download atau print.
- `rejected`: ditolak client.
- `canceled`: dibatalkan dari sisi web/server.

## Error Handling dan Ketahanan

Pola yang dipakai:

- Banyak operasi IO dan network dibungkus `try/catch` agar aplikasi tidak crash.
- Jika config tidak bisa dibaca, default dipakai.
- Jika auth refresh gagal unauthorized, auth lokal dibersihkan.
- Jika heartbeat mendapat 404, client register ulang.
- Jika WebSocket gagal, polling tetap berjalan dan WebSocket mencoba reconnect.
- Jika job list sedang loading, refresh tambahan ditunda lewat `_pendingRefresh`.
- Jika printer offline, job tidak dipaksa print dan status diubah ke `pending`.
- Jika job status berubah sebelum aksi print/reject, aksi dibatalkan dan list refresh.
- File temp dihapus pada jalur sukses dan gagal.

Konsekuensi:

- Aplikasi cenderung fail-soft: memberi status ke user dan lanjut berjalan.
- Beberapa error detail tidak diekspos ke user karena tujuan UI adalah operasional.
- Trace warning dipakai untuk kasus kredit tidak cukup.

## Catatan Keamanan dan Sanitasi Saat Ini

Yang sudah dilakukan:

- Base URL dinormalisasi dan hanya menerima HTTP/HTTPS.
- Client ID dinormalisasi sebagai GUID.
- Path parameter API utama memakai `Uri.EscapeDataString`.
- Nama file download memakai `Path.GetFileName(originalName)` untuk mencegah path traversal langsung.
- PIN unpair dibatasi 4 sampai 8 digit.
- Page range PDF dibatasi ke angka positif atau range angka positif.
- Auth state hanya menyimpan refresh token, user id, dan username; password tidak disimpan.
- UI WinForms menampilkan string sebagai text label, bukan HTML.

Area yang perlu diperkuat jika sistem akan dinaikkan standar ke production hardening:

- Whitelist karakter dan panjang untuk `jobId` dan nama file temp.
- Gunakan `ProcessStartInfo.ArgumentList` untuk argumen proses SumatraPDF/Edge jika memungkinkan.
- Clamp `PrintConfig.ContentScale` sesuai domain yang diharapkan.
- Validasi domain lengkap untuk `PrintConfig`, misalnya copies, paper size, orientation, color mode, dan notes.
- Pertimbangkan proteksi tambahan untuk refresh token lokal, misalnya DPAPI.
- Batasi ukuran file download dan validasi content type bila server mendukung.

## Ringkasan Relasi Modul

Relasi paling penting:

- `Program` membuat lingkungan aplikasi siap, lalu menjalankan `Form1`.
- `AppConfig` menjadi pusat penyimpanan lokal.
- `Form1` menjadi orkestrator: auth, API, printer, realtime, tray, dan print.
- `JobListForm` menjadi UI job dan mendelegasikan aksi sensitif ke `Form1`.
- `LoginForm` dan `UnpairPinDialog` mengambil input user untuk lifecycle pairing.
- `SettingsForm` mengubah konfigurasi yang dibaca `Form1` saat startup.
- `SumatraPdfSupport` menyediakan dependency PDF printing.
- `ToastNotificationForm` dan sound asset memberi feedback realtime.
- `DashboardControls` menjaga konsistensi visual.
- `PrintOrder.iss` membungkus hasil publish menjadi installer.

Jika disederhanakan menjadi sequence besar:

1. Aplikasi start.
2. Config dan client ID disiapkan.
3. Dashboard memuat printer dan auth state.
4. Client register ke server.
5. Jika token ada, sesi dipulihkan.
6. Heartbeat, ping polling, dan WebSocket mulai berjalan.
7. User pairing jika belum terhubung.
8. Server mengirim job lewat API/realtime.
9. Job list memuat dan memfilter job aktif.
10. User print/reject.
11. Client validasi ulang status job ke server.
12. Client update status job ke `printing`, `pending`, `done`, `failed`, atau `rejected`.
13. File job diunduh dan dicetak lokal.
14. Temp file dibersihkan.
15. UI refresh dan server menjadi sumber status terbaru.
