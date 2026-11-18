#!/bin/sh

# You might need to restart your pc if sharun doesn't create `AppDir` in this directory (It should create dirs on its own)
set -eu

# Always build in this script's directory regardless of the caller's cwd
cd "$(dirname "$0")"

ARCH="$(uname -m)"
SHARUN="https://raw.githubusercontent.com/pkgforge-dev/Anylinux-AppImages/refs/heads/main/useful-tools/quick-sharun.sh"
DEBLOATED_PKGS="https://raw.githubusercontent.com/pkgforge-dev/Anylinux-AppImages/refs/heads/main/useful-tools/get-debloated-pkgs.sh"
#export UPINFO="gh-releases-zsync|${GITHUB_REPOSITORY%/*}|${GITHUB_REPOSITORY#*/}|latest|*$ARCH.AppImage.zsync"
export OUTNAME=procon-anylinux-"$ARCH".AppImage
export DESKTOP=../procon.desktop
export ICON=../../procon.png
export OUTPATH=./dist
export DEPLOY_DOTNET=1

#Remove leftovers
rm -rf AppDir dist

# ADD LIBRARIES
wget --retry-connrefused --tries=30 "$SHARUN" -O ./quick-sharun
wget --retry-connrefused --tries=30 "$DEBLOATED_PKGS" -O ./get-debloated-pkgs
chmod +x ./quick-sharun ./get-debloated-pkgs

./get-debloated-pkgs --add-mesa --add-vulkan

# Point to your binaries
./quick-sharun ./*

# Copy rest safely
# DO NOT REPLACE THIS WITH "cp -v ./*.{dll,so,json,pdb} ./AppDir/bin || :" IT CAUSES ISSUES
cp -v ./*.dll ./AppDir/bin || :
cp -v ./*.so ./AppDir/bin || :
cp -v ./*.json ./AppDir/bin || :
cp -v ./*.pdb ./AppDir/bin || :

# relative symlink the same files in AppDir/bin to AppDir/shared/bin
ln -srv "$PWD"/AppDir/bin/*.dll ./AppDir/shared/bin/ 2>/dev/null || :
ln -srv "$PWD"/AppDir/bin/*.so ./AppDir/shared/bin/ 2>/dev/null || :
ln -srv "$PWD"/AppDir/bin/*.json ./AppDir/shared/bin/ 2>/dev/null || :
ln -srv "$PWD"/AppDir/bin/*.pdb ./AppDir/shared/bin/ 2>/dev/null || :

# Make AppImage
./quick-sharun --make-appimage

echo "All Done!"
