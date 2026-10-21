# ติดตั้ง Helpdesk บน Windows Server (SQL Server)

คู่มือนี้สำหรับฝ่าย IT ของบริษัทที่จะติดตั้ง Helpdesk ในเครือข่ายภายใน ใช้ SQL Server เป็นฐานข้อมูล
**ไม่ต้องใช้ IIS และไม่ต้องใช้ Docker** โปรแกรมเดียวทำทั้งหน้าเว็บและ API และรันเป็น Windows Service

## สิ่งที่ต้องมี

| รายการ | รายละเอียด |
|---|---|
| เซิร์ฟเวอร์ | Windows Server 2016 ขึ้นไป (หรือ Windows 10/11) 64 บิต ไม่ต้องติดตั้ง .NET เพราะมากับชุดติดตั้งแล้ว |
| SQL Server | 2017 ขึ้นไป รุ่นใดก็ได้ (Express ใช้ได้) ที่เครื่องนี้หรือเครื่องอื่นที่เข้าถึงได้ |
| sqlcmd | "Microsoft SQL Server command line utilities" ติดตั้งแล้วเปิด PowerShell ใหม่ให้ `sqlcmd` ใช้ได้ |
| สิทธิ์ SQL Server | บัญชีที่สร้างฐานข้อมูลและ login ได้ (เช่น sysadmin) ใช้เฉพาะตอนติดตั้ง |
| พอร์ต | ค่าเริ่มต้น 8080 (เปลี่ยนได้) |
| อีเมลขาออก | ตั้งในหน้าตั้งค่าของแอปภายหลัง (SMTP ขององค์กรเรียกใช้ได้เพราะเซิร์ฟเวอร์อยู่ในเครือข่ายเดียวกัน) |

## ติดตั้งครั้งแรก

1. แตกไฟล์ `helpdesk-server-<เวอร์ชัน>.zip` ไว้ที่เซิร์ฟเวอร์ เช่น `C:\Temp\helpdesk-server`
2. เปิด PowerShell แบบ **Run as administrator** ไปที่โฟลเดอร์นั้น แล้วรัน

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\install.ps1
   ```

3. ตอบคำถาม: ชื่อ SQL Server, ชื่อบริษัท, ที่อยู่ที่ผู้ใช้จะใช้เปิด Helpdesk, อีเมล/ชื่อ/รหัสผ่านของผู้ดูแลคนแรก
4. สคริปต์จะทำให้ครบตามลำดับ แล้วบอก URL ที่เปิดใช้งานได้

ถ้าอยากกำหนดทุกอย่างเอง (ไม่ถามซ้ำ) ใส่เป็นพารามิเตอร์ ดูตัวอย่างด้วย `Get-Help .\install.ps1 -Full`

### สคริปต์ทำอะไรบ้าง

1. **สร้างฐานข้อมูลและบัญชีที่แอปใช้เชื่อมต่อ** (`sqlserver\01-create-database.sql`) ฐานข้อมูลเรียงลำดับตัวอักษรแบบไม่สนตัวพิมพ์ (จำเป็น เพราะอีเมลต้องไม่ซ้ำไม่ว่าตัวพิมพ์เล็กใหญ่) บัญชีของแอปเป็น `db_owner` ของฐานข้อมูลนี้ฐานข้อมูลเดียว ไม่มีสิทธิ์บนส่วนอื่นของเซิร์ฟเวอร์
2. **สร้างตาราง** (`02-schema.sql`)
3. **ใส่บทบาทและสิทธิ์ตั้งต้น** (`03-seed.sql`)
4. คัดลอกโปรแกรมไป `C:\Helpdesk\api` และเขียนไฟล์ตั้งค่า `appsettings.Production.json` (รหัสผ่านฐานข้อมูลและคีย์เซ็น token สร้างสุ่มให้ ไฟล์อ่านได้เฉพาะ Administrators และ service)
5. สร้างผู้ดูแลคนแรก
6. ติดตั้งและเริ่ม Windows Service ชื่อ `Helpdesk` ตั้งให้เริ่มเองเมื่อเปิดเครื่อง และเริ่มใหม่เองถ้าล้ม แล้วตรวจว่าตอบที่ `/health/ready`

สคริปต์ทั้งสามไฟล์รันซ้ำได้ ไม่ทำลายข้อมูลเดิม และถ้าอยากตรวจก่อนก็รันเองด้วย `sqlcmd` ได้ (หัวไฟล์มีคำอธิบาย)
แอปเองก็สร้าง/อัปเดตตารางให้เมื่อเริ่มทำงาน (ปิดได้ด้วย `Database:AutoMigrate = false` ถ้าต้องการรันสคริปต์เองทุกครั้ง)

### เลือกวิธีเชื่อมต่อ SQL Server

- **บัญชี SQL (ค่าเริ่มต้น `-AppAuth Sql`)** สคริปต์สร้าง login `helpdesk_app` พร้อมรหัสผ่านสุ่มให้ SQL Server ต้องเปิดโหมด "SQL Server and Windows Authentication"
- **บัญชี Windows (`-AppAuth Windows`)** แอปเชื่อมด้วยบัญชี Windows ของ service ไม่มีรหัสผ่านฐานข้อมูลในไฟล์ตั้งค่า
  - SQL Server อยู่เครื่องเดียวกัน: ใช้ค่าเริ่มต้น `NT SERVICE\Helpdesk` ได้เลย
  - SQL Server อยู่คนละเครื่อง: ใช้บัญชีโดเมน เช่น `-AppLogin 'CORP\svc-helpdesk' -ServiceAccount 'CORP\svc-helpdesk'` (สคริปต์จะถามรหัสผ่านของบัญชีนั้นถ้าใส่ `-ServiceAccountPassword`)

## หลังติดตั้ง

- เปิด URL ที่สคริปต์แจ้ง ล็อกอินด้วยผู้ดูแลที่สร้างไว้
- **ตั้งค่า → อีเมล (SMTP)** ใส่เซิร์ฟเวอร์ พอร์ต ผู้ใช้ รหัสผ่าน แล้วกด "ส่งอีเมลทดสอบ"
- **ตั้งค่า → ความปลอดภัย** ปรับนโยบายรหัสผ่านตามที่บริษัทต้องการ
- **ตั้งค่า → ผู้ใช้งาน / แผนก / บทบาทและสิทธิ์** เพิ่มคนและกำหนดสิทธิ์
- เปิด firewall ให้พอร์ตของ Helpdesk (`-OpenFirewall` ตอนติดตั้ง หรือ `New-NetFirewallRule` เอง)

## HTTPS

โปรแกรมเปิดพอร์ต HTTP ธรรมดา ถ้าผู้ใช้เข้าผ่านเครือข่ายที่ไม่ปลอดภัย หรือบริษัทกำหนดให้ใช้ HTTPS ทำอย่างใดอย่างหนึ่ง

1. **ใส่ reverse proxy หน้าโปรแกรม** (IIS + ARR, nginx, Caddy หรืออุปกรณ์ของบริษัท) ให้ proxy ถือใบรับรอง แล้วส่งต่อมาที่ `http://localhost:8080` ต้องเปิดรองรับ WebSocket (การแจ้งเตือนสดใช้) และตั้ง `-PublicUrl` เป็นที่อยู่ https ของ proxy
2. **ให้โปรแกรมใช้ใบรับรองเอง** เพิ่มใน `appsettings.Production.json`
   ```json
   "Kestrel": { "Endpoints": { "Https": { "Url": "https://0.0.0.0:8443", "Certificate": { "Path": "C:\\certs\\helpdesk.pfx", "Password": "..." } } } }
   ```
   แล้วรีสตาร์ต service (และแก้ `Urls`/`Cors:Origins`/`App:BaseUrl` ให้ตรง)

ถ้ามี proxy อยู่หน้า โปรแกรมต้องรู้ IP จริงของผู้ใช้ (สำหรับบันทึกและตัวหน่วงการเดารหัส) ตั้ง `LoginThrottle:ClientIpHeader` เป็นชื่อ header ที่ proxy ใส่ เช่น `X-Forwarded-For` **ตั้งเฉพาะเมื่อทุกคำขอผ่าน proxy จริง** ไม่เช่นนั้นผู้ใช้ปลอม header ได้

## อัปเดตเป็นรุ่นใหม่

แตกชุดติดตั้งรุ่นใหม่ แล้วรันใน PowerShell (administrator)

```powershell
.\install.ps1 -Upgrade
```

สคริปต์หยุด service, เก็บโปรแกรมเดิมไว้ที่ `C:\Helpdesk\api.backup-<เวลา>`, ใส่ไฟล์ใหม่, เก็บไฟล์ตั้งค่าเดิม แล้วเริ่ม service ใหม่ ตารางในฐานข้อมูลอัปเดตเองตอนเริ่ม
**สำรองฐานข้อมูลก่อนอัปเดตเสมอ**

## สำรองและกู้คืน

- **สำรอง:** สำรองฐานข้อมูลของ Helpdesk ตามปกติของ SQL Server (`BACKUP DATABASE helpdesk TO DISK = '...'` หรือ Maintenance Plan) ไฟล์แนบ (รูป) **เก็บอยู่ในฐานข้อมูล** จึงอยู่ในไฟล์สำรองเดียวกัน
- **สำรองไฟล์ตั้งค่า** `C:\Helpdesk\api\appsettings.Production.json` ไว้ที่ปลอดภัย (มีคีย์เซ็น token ถ้าหายต้องให้ทุกคนล็อกอินใหม่ และรหัสผ่านอีเมลที่เก็บในระบบจะอ่านไม่ได้ ต้องกรอกใหม่)

## ถอนการติดตั้ง

```powershell
Stop-Service Helpdesk; sc.exe delete Helpdesk
Remove-Item C:\Helpdesk -Recurse
```
ฐานข้อมูลไม่ถูกลบ ถ้าต้องการให้ลบเองใน SQL Server (`DROP DATABASE`, `DROP LOGIN`)

## แก้ปัญหา

| อาการ | ตรวจ |
|---|---|
| `install.ps1` บอกหา sqlcmd ไม่เจอ | ติดตั้ง sqlcmd แล้วเปิด PowerShell ใหม่ |
| ขั้นสร้างฐานข้อมูลล้ม | บัญชีที่รันต้องสร้างฐานข้อมูลและ login ได้ ถ้าใช้ SQL login ให้ใส่ `-SqlAdminUser` |
| service เริ่มแล้วไม่ตอบ | Event Viewer → Windows Logs → Application (source `Helpdesk`) ส่วนใหญ่คือที่อยู่ SQL Server หรือบัญชีเชื่อมต่อผิด |
| เปิดหน้าเว็บได้แต่ล็อกอินแล้วขึ้นว่าเชื่อมต่อไม่ได้ | `-PublicUrl` ต้องตรงกับที่อยู่ที่ผู้ใช้พิมพ์จริง (รวม http/https และพอร์ต) |
| อีเมลไม่ออก | ตั้งค่า → อีเมล (SMTP) → ส่งอีเมลทดสอบ ดูข้อความจากเมลเซิร์ฟเวอร์ |

## ใช้ Docker แทนได้ไหม

ได้ โปรแกรมเป็น ASP.NET Core ที่เชื่อม SQL Server ได้ทั้งใน Windows และ Linux มี `Dockerfile` อยู่ใน `backend/KPI.API` ให้ build image แล้วตั้งตัวแปร `Database__Provider=SqlServer`,
`ConnectionStrings__DefaultConnection`, `Jwt__Key`, `Cors__Origins__0` และสร้างผู้ดูแลคนแรกด้วย `docker run --rm -e HELPDESK_ADMIN_EMAIL=... <image> create-admin`
คู่มือนี้ทดสอบเฉพาะวิธี Windows Service
