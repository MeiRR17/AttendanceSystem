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
Update NotificationService.cs with your Twilio Account SID, Auth Token and students' WhatsApp number.
```
private readonly string accountSid = "your_account_sid"; // Your actual SID
private readonly string authToken = "your_auth_token"; // Your actual Auth Token
```

### 5. Build and Run the Project
* Open the project in Visual Studio.
* Build the project by pressing Ctrl+Shift+B.
* Run the project by pressing Ctrl+F5.

### 6. Set Up the Scheduled Task (Changeable)
Automate the project to run at 1:00PM on Monday, Tuesday, Wednesday and Thursday using Windows Task Scheduler.

* Create a Batch File
 1. Open Notepad.
 2. Enter the following content, adjusting the path to match your project directory:
```
@echo off
cd /d "C:\PathToProject"   // Change this path to your actual project directory (check on your system)
start AttendanceSystem.exe
```
 3. Save the file with a .bat extension, e.g., RunAttendanceSystem.bat.
* Set Up Task Scheduler
 1. Open Task Scheduler:
    * Press Win + R to open the Run dialog.
    * Type taskschd.msc and press Enter to open Task Scheduler.
 2. Create a New Task:
    * In Task Scheduler, click on Create Task in the Actions pane.
 3. General Settings:
    * Name: Enter a name for the task, e.g., "Run Attendance System".
    * Description: Optionally enter a description.
    * Security Options: Select "Run whether user is logged on or not" and "Run with highest privileges".
 4. Triggers:
    * Click on the Triggers tab.
    * Click New to create a new trigger.
    * Set the Begin the task dropdown to "On a schedule".
    * Set the Settings to "Weekly".
    * Check Monday, Tuesday, Wednesday, and Thursday.
    * Set the Start time to "1:00 PM".
    * Click OK to save the trigger.
 5. Actions:
    * Click on the Actions tab.
    * Click New to create a new action.
    * Set the Action dropdown to "Start a program".
    * In the Program/script field, click Browse and select the RunAttendanceSystem.bat file you created.
    * Click OK to save the action.
 6. Conditions:
    * Click on the Conditions tab.
    * Adjust any conditions as needed (e.g., ensure the task runs only if the computer is on AC power).
 7. Settings:
    * Click on the Settings tab.
    * Ensure "Allow task to be run on demand" and "If the task fails, restart every" are checked with a V.
 8. Save the Task:
    * Click OK to save the task.
### 7. Usage
Run the project.
