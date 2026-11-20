Name:           nashi
Version:        0.1.0
Release:        1%{?dist}
Summary:        Nashi file toolkit — experimental Fedora port
License:        MIT AND Apache-2.0 AND BSD-3-Clause
URL:            https://github.com/yasx01/nashi-app
Source0:        nashi-linux-x64.tar.gz
ExclusiveArch:  x86_64
AutoReqProv:    no
Requires:       glibc, libstdc++, zlib, openssl-libs, libicu
Requires:       fontconfig, libX11, libICE, libSM, libXrandr, libXi, libXcursor
Requires:       mesa-libGL, xorg-x11-server-Xwayland
Recommends:     libreoffice

%global debug_package %{nil}
%global __os_install_post %{nil}

%description
An independent Fedora desktop port of Nashi, reusing its original artwork
and cross-platform processing engine. This first build does not provide full
feature parity with the macOS or Windows editions. See the included README.
The .NET runtime is included. Media tool downloads remain optional.

%prep
%setup -q -c

%build

%install
mkdir -p %{buildroot}/opt/nashi
cp -a . %{buildroot}/opt/nashi/
install -Dm644 nashi.desktop %{buildroot}%{_datadir}/applications/nashi.desktop
install -Dm644 NashiIcon.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/nashi.png

%files
/opt/nashi
%{_datadir}/applications/nashi.desktop
%{_datadir}/icons/hicolor/256x256/apps/nashi.png

%changelog
* Fri Nov 20 2026 Nashi Fedora contributors - 0.1.0-1
- Initial experimental Fedora desktop package.
