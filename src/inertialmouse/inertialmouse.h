/*++
Module Name:

    inertialmouse.h

Abstract:

    Shared declarations for the inertial HID mouse filter.

--*/

#ifndef INERTIALMOUSE_H
#define INERTIALMOUSE_H

#include <ntddk.h>
#include <wdf.h>
#include <kbdmou.h>
#include <ntddmou.h>

#define IM_FIXED_SHIFT             8
#define IM_FIXED_SCALE             (1 << IM_FIXED_SHIFT)

#define IM_DEFAULT_ENABLED         TRUE
#define IM_DEFAULT_TIMER_MS        8
#define IM_DEFAULT_ACCEL_X_Q8      (1 * IM_FIXED_SCALE)
#define IM_DEFAULT_ACCEL_Y_Q8      (1 * IM_FIXED_SCALE)
#define IM_DEFAULT_FRICTION_X_Q8   20
#define IM_DEFAULT_FRICTION_Y_Q8   20
#define IM_DEFAULT_MAX_VEL_X_Q8    (40 * IM_FIXED_SCALE)
#define IM_DEFAULT_MAX_VEL_Y_Q8    (40 * IM_FIXED_SCALE)

#define IM_MIN_TIMER_MS            1
#define IM_MAX_TIMER_MS            100
#define IM_MIN_Q8                  1
#define IM_MAX_Q8                  (1024 * IM_FIXED_SCALE)

typedef struct _IM_CONFIG
{
    BOOLEAN Enabled;
    ULONG TimerPeriodMs;
    LONG AccelXQ8;
    LONG AccelYQ8;
    LONG FrictionXQ8;
    LONG FrictionYQ8;
    LONG MaxVelocityXQ8;
    LONG MaxVelocityYQ8;
} IM_CONFIG, *PIM_CONFIG;

typedef struct _IM_MOTION_STATE
{
    BOOLEAN HasPacketTemplate;
    LONG VelocityXQ8;
    LONG VelocityYQ8;
    LONG ResidualXQ8;
    LONG ResidualYQ8;
    MOUSE_INPUT_DATA PacketTemplate;
} IM_MOTION_STATE, *PIM_MOTION_STATE;

typedef struct _IM_DEVICE_CONTEXT
{
    CONNECT_DATA UpperConnectData;
    WDFTIMER FrictionTimer;
    WDFSPINLOCK MotionLock;
    IM_CONFIG Config;
    IM_MOTION_STATE Motion;
} IM_DEVICE_CONTEXT, *PIM_DEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(IM_DEVICE_CONTEXT, ImGetDeviceContext)

DRIVER_INITIALIZE DriverEntry;

#endif
