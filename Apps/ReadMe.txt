SitCore SC20260N (SC20) Based Meter Application Code
(Most recent notes at top)

App_2.2.1.2_20240320.tca
==========================
GHI have done some more firmware updates, DiskDriveMode should work on a wider range of hardware now.
RebootToMs settings wasn't saved correctly to config file,fixed.
Various other minor fixes

App_2.2.0.7_20240308-2.tca
==========================
Unfortunately 2.2.0.7000 didn't fix the MassStorage switch problem.
I've added a config setting (FactoryDefaults.csv, setable from the meter setup menu), to enable or disable forcing a reboot when switching modes.
If your PCs will switch without needing the reboot, keep it in the "no" position!

App_2.2.0.7_20240308-1.tca
==========================
Upgrade to GHI's latest 2.2.0.7000 firmware, which hopefully has fixed switch to mass storage (DiskDriveMode) and RTC wake from sleep.
Includes updates from G120 version to work with downloading large schedule files over WiFi, and diagnostics force schedule download on long-Down press,
and KeepOldFiles

App_2.2.0.6_20240114-1.tca
==========================
GHI's USB MassStorage switch fix doesn't work for all PCs. In testing on 7 PCs here, it worked on 3 of the 7.
This version goes back to rebooting to switch between modes until GHI comes up with a reliable fix.

App_2.2.0.6_20240111-1.tca
==========================
This version written for GHI's release v2.2.0.6200 which was meant to fix the RTC sleep/wake problem, but seems it hasn't.
It contains a fix for updating Apps where the firmware version is still compatible, but a newer versionis available. (eg 2.2.0.6100 to 2.2.0.6200)

App_2.2.0.6_20240105-1.tca
==========================
Fixed Tx data chunking on ESP12, specs say max packet size 2048 but that didn't work, currently set to 1000 bytes
AnodeMeterService also updated this date

App_2.2.0.6_20240103-1.tca
==========================
Increase TinyCLR UART Rx buffer size to avoid overrun reading from ESP12
(Fixes meter config and schedule downloads)
Long-press on Down button will trigger config check and start Wifi

App_2.2.0.6_20231230-1.tca
==========================
Adds StaticIP to config (FactoryDefaults.csv)
Mods to allow use of >=64GB uSD card (Formatted as Large FAT32). Still may get better results with <=32GB cards
Display WiFi MAC address

Upgrading from firmware 2.2.0.5100 to 2.2.0.6100 (App_2.2.0 to App_2.2.0.6)
===========================================================================
1. Ensure meter is running a recent version of App [Setup->Meter Readings displays "Board: SC20260 FW 2.2.0" (NOT 2.2.0.5100) Built: 2023-01-13 or newer
   (Most/all released meters should be >= this version, if not upgrade first)
2. Install subdirectories 2.2.x, 2.2.0.5 and 2.2.0.6 in Updates->SC20
3. Install transition version App_2.2.x_20281013-2X.tca in Updates\SC20 folder.
  (Note: no other .tca files can be in this folder. Create a subdirectory \Apps and put an others in there, or risk locking the board)
4. Go to Meter Support->Upgrade Software

App_2.2.0.6_20231013-2.tca
==========================
Requires GHI fixed firmware 2.2.0.6100 and above (USB MassStorage switch fix, RTC wakeup fix)
Adds selection of App from multiple versions in top level SC20 folder andSC20/Apps folder

App_2.2.x_20231013-2X.tca
=========================
This is the same as the above version, but name changed so it will load from older versions running 2.2.0.5100 firmware
To work this must be the ONLY .tca file in the top level SC20 folder, put the others in SC20/Apps

DAV 13 October 2023

App_2.2.0_20230928-1.tca (Don't use, use the one above instead!)
========================
This is a transition version which needs to be installed before upgrading to the 2.2.0.6xxx firmware.
It is the same as the previous version, except that it matches based on the relevant version codes, so 2.2.0.5xxx, with next version being upgrade to 2.2.0.6xxx

DAV 28 Sep 2023  

App_2.2.0_20230621-1.tca
========================
Peter's meter config program wrote UTF-16 headers on the SmelterConfiguration.xml, despite it only being 8-bit ASCII, which caused a problem, as the file was downloaded
from the server, and then couldn't be read. I added a check to make sure it is utf-8. His program should be fixed by now.
I increased the tank filter size on the battery voltage reading as the ADC onthe SC20 is way noisier than the older boards.
The SC20 has a unique ID, I display that inmeter readings now.

DAV 21 June 2023

App_2.2.0_20230314-1.tca
========================
Testing the previous version at Portland found an incompatability with Alcoa build PCs (most likely hardware) which prevented the meters going into Disk Drive mode.
This version has a workaround which has the meter reboot to change between normal and DiskDrive USB modes, again until GHI fixes the SDK
There are also some fixes required due to file system changes between NetMF and TinyCLR, and some bugfixes.
There is now a "Wifi Disable" option accessible from the meter setup menu, as well as a couple of WiFi diagnostic modes (development only)

If you cannot get your meter to go into Disk drive mode to install this update, you will have to remove the microSD card and copy the file across using a normal card reader.

We may add an OTA (Over The Air) program upgrade capability (USB and/or Wifi)directly from the server, if there is any user interest in this, please let me know.

DAV 14 March 2023


App_2.2.0_20230129-1.tca
========================
This version has a workaround for the problem of needing to disconnect when switching between WinUSB and DiskDrive modes
It does NOT have a working fix for the problems with Hibernate mode, we are waiting on GHI for this.
For now please disable Sleep mode in FactoryDefaults.csv (set it to 0, or a long time so it will never be called)

There are also a couple of minor fixes.
One with the Battery Life calculation, where files created with EMX or G120 versions could not be read. (TinyCLR uses a different name for the root file system)
Another cleans up version info given on long-press of the Select button

DAV 29 Jan 2023


00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00
