dotnet build -c Release
rm -rf local_mod/*
cp -r About local_mod/
cp bin/Release/netstandard2.1/IC10Editor.dll local_mod/
