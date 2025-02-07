#!/bin/bash
# Run from git root

SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )

set -e
set -x

dpkg --add-architecture i386
apt-get update
apt-get install -y npm dotnet-sdk-6.0 build-essential binutils lintian debhelper dh-make devscripts xmlstarlet # needs cleanup probably, SO copypasta

CURRENT_COMMIT=$(git rev-parse HEAD)

rm -rf packaging

set +e
git worktree remove -f packaging
set -e

git worktree add -f packaging $CURRENT_COMMIT
cd packaging
rm -f .git

export DEBEMAIL="Cyberboss@users.noreply.github.com"
export DEBFULLNAME="Jordan Dominion"

TGS_VERSION=$(xmlstarlet sel -N X="http://schemas.microsoft.com/developer/msbuild/2003" --template --value-of /X:Project/X:PropertyGroup/X:TgsCoreVersion build/Version.props)

dh_make -p tgstation-server_$TGS_VERSION -y --createorig -s

rm -f debian/README* debian/changelog debian/*.ex debian/upstream/*.ex

pushd ..
dotnet run -c Release -p:TGS_HOST_NO_WEBPANEL=true --project tools/Tgstation.Server.ReleaseNotes $TGS_VERSION --debian packaging/debian/changelog $CURRENT_COMMIT
popd

cp -r build/package/deb/debian/* debian/

cp build/tgstation-server.service debian/

SIGN_COMMAND="$SCRIPT_DIR/wrap_gpg.sh"

rm -f /tmp/tgs_wrap_gpg_output.log

set +e

if [[ -z "$PACKAGING_KEYGRIP" ]]; then
    dpkg-buildpackage --no-sign
else
    dpkg-buildpackage --sign-key=$PACKAGING_KEYGRIP --sign-command="$SIGN_COMMAND"
fi

EXIT_CODE=$?

cat /tmp/tgs_wrap_gpg_output.log

cd ..

exit $EXIT_CODE
