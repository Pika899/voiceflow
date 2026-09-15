#!/bin/bash
# Creates a local, self-signed code-signing certificate named "VoiceFlow Dev"
# in the login keychain, if one doesn't exist yet.
#
# Why: an ad-hoc signature (`codesign -s -`) identifies the app by the hash
# of its exact bytes, so every rebuild looks like a brand-new app to macOS
# and the Accessibility/Microphone grants silently stop applying (the toggle
# still shows ON). A certificate-based signature keeps the same identity
# across rebuilds, so permissions granted once keep working.
#
# This certificate is for local development on this Mac only. It is not a
# Developer ID and does nothing for distribution or notarization.
set -euo pipefail

NAME="VoiceFlow Dev"
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"

if security find-identity -v -p codesigning "$KEYCHAIN" 2>/dev/null | grep -q "\"$NAME\""; then
    echo "Identity \"$NAME\" already exists."
    exit 0
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

cat > "$WORK/ext.cnf" <<CNF
[req]
distinguished_name = dn
x509_extensions = v3
prompt = no
[dn]
CN = $NAME
[v3]
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
basicConstraints = critical, CA:false
subjectKeyIdentifier = hash
CNF

openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
    -keyout "$WORK/key.pem" -out "$WORK/cert.pem" -config "$WORK/ext.cnf" >/dev/null 2>&1
openssl pkcs12 -export -inkey "$WORK/key.pem" -in "$WORK/cert.pem" \
    -name "$NAME" -passout pass:voiceflow -out "$WORK/cert.p12" >/dev/null 2>&1

security import "$WORK/cert.p12" -k "$KEYCHAIN" -P voiceflow \
    -T /usr/bin/codesign -T /usr/bin/security >/dev/null

# Mark the certificate trusted for code signing in the user's trust settings.
# macOS may show a password dialog once for this step.
security add-trusted-cert -r trustRoot -p codeSign -k "$KEYCHAIN" "$WORK/cert.pem"

echo "Created identity \"$NAME\":"
security find-identity -v -p codesigning "$KEYCHAIN" | grep "$NAME"
