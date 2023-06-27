# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# Install preview on ubuntu
# Update the list of packages
write-progress "apt update" -perc 10
apt -qq update
# Install pre-requisite packages.
write-progress "apt install vim wget https common" -perc 20
$null = apt -qq install -y vim wget apt-transport-https software-properties-common 2>&1
# Download the Microsoft repository GPG keys
write-progress "wget" -perc 40
wget -q "https://packages.microsoft.com/config/ubuntu/$(/usr/bin/lsb_release -rs)/packages-microsoft-prod.deb"
# Register the Microsoft repository GPG keys
write-progress "register repository GPG keys" -perc 50
dpkg -i packages-microsoft-prod.deb
# Delete the the Microsoft repository GPG keys file
rm packages-microsoft-prod.deb
# Update the list of packages after we added packages.microsoft.com
write-progress "apt update again" -perc 70
apt -qq update
# Install PowerShell
write-progress "apt install powershell-preview" -perc 90
$null = apt -qq install -y powershell-preview 2>&1
