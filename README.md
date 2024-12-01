# Attendance System

This project is an automated attendance system that uses a fingerprint scanner to record student attendance. It checks for Hebrew holidays and weekends to skip attendance recording and sends notifications to students who are late using Twilio's WhatsApp messaging service.

## Features

- Fingerprint scanner integration to capture student attendance
- Checks for Hebrew holidays and weekends to skip attendance
- Sends notifications to late students using Twilio

## Prerequisites

- .NET Framework 4.7.2 or later
- SQLite
- Twilio account
- Hebcal API for fetching Hebrew holidays

## Setup

### 1. Clone the Repository

To get a local copy of the project up and running, you can clone it using Git:

```bash
git clone https://github.com/yourusername/AttendanceSystem.git
cd AttendanceSystem
```
### 2. Install Dependencies
* Open the project in Visual Studio.
* Install the necessary NuGet packages:
  * Newtonsoft.Json
  * Twilio

### 3. Set Up the Database
Ensure attendance.db is in the project root directory.
The database will be initialized when you run the project for the first time.

### 4. Configure Twilio
Update NotificationService.cs with your Twilio Account SID, Auth Token, and WhatsApp number.
```
private readonly string accountSid = "your_account_sid"; // Your actual SID
private readonly string authToken = "your_auth_token"; // Your actual Auth Token
```
### 5. Set Up Hebcal API (optional)
The API integration is handled in HolidayFetcher.cs.
Ensure you have the correct URL for fetching Hebrew holidays.

### 6. Build and Run the Project
* Open the project in Visual Studio.
* Build the project by pressing Ctrl+Shift+B.
* Run the project by pressing Ctrl+F5.

### 7. Set Up the Scheduled Task (Changeable)
Automate the project to run at 1:00PM on Monday, Tuesday, Wednesday and Thursday using Windows Task Scheduler.

Create a Batch File:
```
@echo off
cd /d "C:\PathToProject"   // Change this path to your actual project directory (check on your system)
start AttendanceSystem.exe
```
### 8. Usage
Run the project.
