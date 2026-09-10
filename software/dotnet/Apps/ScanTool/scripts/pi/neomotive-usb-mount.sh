#!/bin/bash
# Mounts a USB partition read-only at /media/usb so the app's update scanner can
# find packages on it. UsbUpdateSource.HasRemovableDrive() tests /proc/mounts for
# exactly that mount point and bails before scanning anything else.
#
# Invoked by neomotive-usb-mount@.service, which udev pulls in with
# ENV{SYSTEMD_WANTS} when a USB filesystem partition appears.
#
# It is NOT called from a udev RUN+= rule any more, and must not be. systemd-udevd
# gives its workers a private mount namespace: `mount` from a RUN rule fails on
# this image with a bare "mount: /media/usb: permission denied", and on the
# systemd versions where it succeeds the mount is invisible to every other
# process — including the app. Both failure modes look identical from the app's
# side (/media/usb never appears), which is why this looked like a dead udev rule
# for so long. Handing the work to a systemd unit puts the mount in the host
# namespace, where the app can see it.
#
# Keep this file identical between Apps/ScanTool/scripts/pi and
# Apps/ModuleSimulator/scripts/pi — two copies, one per device image.

set -u

ACTION="${1:-}"
DEVICE="${2:-}"
MOUNT_POINT="/media/usb"
TAG="neomotive-usb-mount"

# -t so `journalctl -t neomotive-usb-mount` actually finds these. A bare `logger`
# tags with the calling user, so the documented troubleshooting command returned
# "no entries" even on the runs that worked.
log() { logger -t "$TAG" -- "$*"; echo "$TAG: $*"; }

case "$ACTION" in
    add)
        [ -n "$DEVICE" ]        || { log "add: no device given"; exit 1; }
        [ -b "/dev/$DEVICE" ]   || { log "add: /dev/$DEVICE is not a block device"; exit 1; }

        mkdir -p "$MOUNT_POINT"

        # A stick swapped without a clean removal leaves the old mount behind,
        # and mounting over it would hide the new one from the app.
        if findmnt -n "$MOUNT_POINT" >/dev/null 2>&1; then
            log "add: $MOUNT_POINT already mounted — unmounting first"
            umount "$MOUNT_POINT" 2>/dev/null || umount -l "$MOUNT_POINT" 2>/dev/null || true
        fi

        # uid/gid 1000 is the appliance user: vfat and exfat carry no ownership,
        # so without this the app cannot read the package it just found. The
        # bare `-o ro` last covers ext4/ntfs sticks the first two do not.
        # Unquoted $opts on purpose — these are several arguments, not one.
        for opts in "-t vfat -o ro,uid=1000,gid=1000" \
                    "-t exfat -o ro,uid=1000,gid=1000" \
                    "-o ro"; do
            # shellcheck disable=SC2086
            if mount $opts "/dev/$DEVICE" "$MOUNT_POINT" 2>/dev/null; then
                log "mounted /dev/$DEVICE at $MOUNT_POINT ($opts)"
                exit 0
            fi
        done

        # Report the real error and fail. The old version ended every attempt with
        # `|| true` and then logged "mounted" unconditionally, so a stick that
        # never mounted still reported success.
        err="$(mount -o ro "/dev/$DEVICE" "$MOUNT_POINT" 2>&1 | tr '\n' ' ')"
        log "FAILED to mount /dev/$DEVICE at $MOUNT_POINT: $err"
        exit 1
        ;;
    remove)
        if findmnt -n "$MOUNT_POINT" >/dev/null 2>&1; then
            if umount -l "$MOUNT_POINT" 2>/dev/null; then
                log "unmounted $MOUNT_POINT"
            else
                log "could not unmount $MOUNT_POINT"
            fi
        fi
        ;;
    *)
        echo "usage: $0 {add|remove} <kernel-device-name>" >&2
        exit 2
        ;;
esac
