@echo off
title Enable Smart Skii-mer phone remote
echo This one-time step lets your phone control Smart Skii-mer on your home Wi-Fi.
echo A Windows prompt will ask for administrator permission.
echo.
netsh http add urlacl url=http://+:47821/ user=%USERDOMAIN%\%USERNAME%
netsh advfirewall firewall delete rule name="Smart Skii-mer Remote" >nul 2>&1
netsh advfirewall firewall add rule name="Smart Skii-mer Remote" dir=in action=allow protocol=TCP localport=47821 profile=private
echo.
echo Done. Open Smart Skii-mer, go to Settings, and turn on "Phone remote".
pause
