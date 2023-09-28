SitCore SC20260N (SC20) Based Meter Application Code
(Most recent notes at top)

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

App_2.2.0_20230928-1.tca
========================
This is a transition version which needs to be installed before upgrading to the 2.2.0.6xxx firmware.
It is the same as the previous version, except that it matches based on the relevant version codes, so 2.2.0.5xxx, with next version being upgrade to 2.2.0.6xxx

DAV 28 Sep 2023  

00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00
