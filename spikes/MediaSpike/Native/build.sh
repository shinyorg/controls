#!/bin/zsh
# SPIKE: build ShinySpeechBridge.swift into a dynamic framework per slice and wrap them in an xcframework.
set -e
cd "${0:A:h}"
NAME=ShinySpeechBridge
OUT=build
rm -rf $OUT && mkdir -p $OUT

slice() { # sdk target dir
  local sdk=$1 target=$2 dir=$OUT/$3/$NAME.framework
  mkdir -p $dir
  xcrun -sdk $sdk swiftc ShinySpeechBridge.swift \
    -target $target -sdk "$(xcrun --sdk $sdk --show-sdk-path)" \
    -module-name $NAME -emit-library -O -swift-version 5 \
    -Xlinker -install_name -Xlinker @rpath/$NAME.framework/$NAME \
    -o $dir/$NAME
  cat > $dir/Info.plist <<EOF
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
}

slice iphoneos        arm64-apple-ios15.0           ios-arm64
slice iphonesimulator arm64-apple-ios15.0-simulator ios-arm64-simulator

xcodebuild -create-xcframework \
  -framework $OUT/ios-arm64/$NAME.framework \
  -framework $OUT/ios-arm64-simulator/$NAME.framework \
  -output $OUT/$NAME.xcframework
echo "built $OUT/$NAME.xcframework"

for sdk in iphoneos iphonesimulator; do
  xcrun -sdk $sdk metal -c -fcikernel crt.metal -o $OUT/crt-$sdk.air
  xcrun -sdk $sdk metallib -cikernel $OUT/crt-$sdk.air -o $OUT/crt-$sdk.metallib
done
echo "built crt metallibs"
