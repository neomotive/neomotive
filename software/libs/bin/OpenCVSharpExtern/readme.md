# libOpenCvSharpExtern.so

## Linux placement

Linux uses a different library loading algorithm than Windows, so just putting this lib into the app folder will not work.

You have a few options.  

1. You can put in in a default system path like `/lib` or `/usr/lib`
2. You can `sudo cp` it to `/usr/local/lib` and then run `sudo ldconfig` which adds it to the global `/etc/ld.so.cache`
3. You can add it to the `LD_LIBRARY_PATH` environment variable before running
   `$ export LD_LIBRARY_PATH="/my/app/folder:$LD_LIBRARY_PATH"`
