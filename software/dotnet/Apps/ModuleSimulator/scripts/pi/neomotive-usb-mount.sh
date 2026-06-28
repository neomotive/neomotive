#!/bin/bash
# Called by udev when a USB block device is added or removed.
# Mounts the device read-only at /media/usb so the app can scan it for update packages.
ACTION="$1"
DEVICE="$2"
MOUNT_POINT="/media/usb"

case "$ACTION" in
    add)
        # Wait for the block device to finish initializing before mounting.
        # udev fires when the partition node is created, but the storage device
        # may not yet be ready for I/O — settle blocks until the queue drains.
        /sbin/udevadm settle --exit-if-exists="/dev/$DEVICE" --timeout=10
        sleep 1
        mkdir -p "$MOUNT_POINT"
        # Try vfat (FAT32), then exfat, then let the kernel guess
        /bin/mount -o ro,uid=1000,gid=1000 -t vfat  "/dev/$DEVICE" "$MOUNT_POINT" 2>/dev/null || \
        /bin/mount -o ro,uid=1000,gid=1000 -t exfat "/dev/$DEVICE" "$MOUNT_POINT" 2>/dev/null || \
        /bin/mount -o ro                             "/dev/$DEVICE" "$MOUNT_POINT" 2>/dev/null || true
        logger "neomotive-usb-mount: mounted /dev/$DEVICE at $MOUNT_POINT"
        ;;
    remove)
        /bin/umount -l "$MOUNT_POINT" 2>/dev/null || true
        logger "neomotive-usb-mount: unmounted $MOUNT_POINT"
        ;;
esac
