#!/bin/zsh
# Builds ShinyFoundationModels.swift into ShinyFoundationModels.xcframework.zip (iOS device, iOS simulator,
# Mac Catalyst, macOS). The zip is committed: CI builds the packages on Windows, which cannot compile Swift,
# so re-run this on a Mac (Xcode 27.2+) whenever the .swift file changes and commit the result. It is a zip
# rather than the folder because the macOS-style framework bundles are made of symlinks, which a Windows
# checkout turns into text files; the SDK takes a NativeReference to an .xcframework.zip directly.
set -e
cd "${0:A:h}"
NAME=ShinyFoundationModels
OUT=build
rm -rf $OUT $NAME.xcframework $NAME.xcframework.zip && mkdir -p $OUT

# sdk  dir  min-os-target-suffix  versioned(1 for macOS-style bundles)  archs...
slice() {
  local sdk=$1 dir=$2 triple=$3 versioned=$4; shift 4
  local fw=$OUT/$dir/$NAME.framework bin_dir res_dir
  if [ "$versioned" = 1 ]; then
    bin_dir=$fw/Versions/A; res_dir=$fw/Versions/A/Resources
  else
    bin_dir=$fw; res_dir=$fw
  fi
  mkdir -p $bin_dir $res_dir

  local thin=()
  for arch in "$@"; do
    xcrun -sdk $sdk swiftc $NAME.swift \
      -target ${arch}-${triple} -sdk "$(xcrun --sdk $sdk --show-sdk-path)" \
      -module-name $NAME -emit-library -O -swift-version 5 \
      -Xlinker -install_name -Xlinker @rpath/$NAME.framework/$( [ "$versioned" = 1 ] && echo "Versions/A/" )$NAME \
      -o $OUT/$dir/$NAME-$arch
    thin+=($OUT/$dir/$NAME-$arch)
  done
  lipo -create $thin -output $bin_dir/$NAME
  rm $thin

  cat > $res_dir/Info.plist <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>$NAME</string>
  <key>CFBundleIdentifier</key><string>dev.shiny.$NAME</string>
  <key>CFBundleName</key><string>$NAME</string>
  <key>CFBundlePackageType</key><string>FMWK</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>MinimumOSVersion</key><string>15.0</string>
</dict></plist>
EOF

  if [ "$versioned" = 1 ]; then
    ln -s A $fw/Versions/Current
    ln -s Versions/Current/$NAME $fw/$NAME
    ln -s Versions/Current/Resources $fw/Resources
  fi
}

# FoundationModels is weak-linked below 26 (the @available guards return "unavailable"), so the
# deployment targets match the packages', not the framework's.
slice iphoneos        ios-arm64                  apple-ios15.0           0 arm64
slice iphonesimulator ios-arm64_x86_64-simulator apple-ios15.0-simulator 0 arm64 x86_64
slice macosx          ios-arm64_x86_64-maccatalyst apple-ios15.0-macabi  1 arm64 x86_64
slice macosx          macos-arm64_x86_64         apple-macos12.0         1 arm64 x86_64

xcodebuild -create-xcframework \
  -framework $OUT/ios-arm64/$NAME.framework \
  -framework $OUT/ios-arm64_x86_64-simulator/$NAME.framework \
  -framework $OUT/ios-arm64_x86_64-maccatalyst/$NAME.framework \
  -framework $OUT/macos-arm64_x86_64/$NAME.framework \
  -output $NAME.xcframework
# ditto keeps the symlinks as symlinks
ditto -c -k --keepParent $NAME.xcframework $NAME.xcframework.zip
rm -rf $OUT $NAME.xcframework
echo "built $NAME.xcframework.zip"
