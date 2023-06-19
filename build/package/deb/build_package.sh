#!/bin/bash
# Run from git root

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

rm debian/README* debian/changelog debian/*.ex debian/upstream/*.ex

cp -r build/package/deb/debian/* debian/
sed -i "s/~!VERSION!~/$TGS_VERSION/g" debian/changelog

cp build/tgstation-server.service debian/

dpkg-buildpackage

cd ..
