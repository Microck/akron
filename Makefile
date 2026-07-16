.PHONY: build clean format format-check package preflight-release restore test

restore:
	dotnet restore Akron.sln --nologo

build:
	dotnet build Akron.sln --configuration Release --nologo

test:
	dotnet test tests/akron-tests.csproj --configuration Release --nologo

format:
	dotnet format Akron.sln --include Source/Core/AkronFeatureRegistry.cs tests/feature-registry-tests.cs

format-check:
	dotnet format Akron.sln --include Source/Core/AkronFeatureRegistry.cs tests/feature-registry-tests.cs --verify-no-changes

package:
	dotnet build Source/Akron.csproj --configuration Release --nologo
	test -f Akron.zip
	unzip -t Akron.zip
	unzip -Z1 Akron.zip | grep -Fx LICENSE >/dev/null
	unzip -Z1 Akron.zip | grep -Fx ThirdPartyNotices.txt >/dev/null
	unzip -p Akron.zip LICENSE | cmp LICENSE -
	unzip -p Akron.zip ThirdPartyNotices.txt | cmp licenses/third-party-notices.txt -

preflight-release: restore format-check build test package

clean:
	dotnet clean Akron.sln --nologo
	rm -rf bin Source/bin Source/obj tests/bin tests/obj Akron.zip
