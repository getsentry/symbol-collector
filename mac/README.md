currently just a helper script to search for osx/macos updata packages.

usage:

```
$ ./dmgmonkey.py "10.10"

{
 "count": 153,
 "packages": [
  {
   "url": "https://updates.cdn-apple.com/2019/cert/041-88380-20191011-c5bbb3ec-7f37-4b44-8cb4-753810df3edc/OSXUpdCombo10.10.5.dmg",
   "title": "Download OS X Yosemite 10.10.5 Combo Update"
  },
  {
   "url": "https://updates.cdn-apple.com/2019/cert/041-84845-20191010-2bbc14ed-c048-43aa-bfb5-c88a8f27fb61/BashUpdateLion.dmg",
   "title": "OS X bash Update 1.0 - OS X Lion & OS X Lion Server"
  },
...
```

for complete output see `sampleoutput.txt`