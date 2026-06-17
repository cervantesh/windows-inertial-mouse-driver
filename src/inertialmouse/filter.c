/*++
Module Name:

    filter.c

Abstract:

    Mouse class service callback and friction timer.

--*/

#include "filter.h"
#include "motion.h"

VOID
ImServiceCallback(
    _In_ PDEVICE_OBJECT DeviceObject,
    _In_ PMOUSE_INPUT_DATA InputDataStart,
    _In_ PMOUSE_INPUT_DATA InputDataEnd,
    _Inout_ PULONG InputDataConsumed
    )
{
    PIM_DEVICE_CONTEXT context;
    WDFDEVICE device;
    ULONG count;
    ULONG index;
    BOOLEAN transformed;

    device = WdfWdmDeviceGetWdfDeviceHandle(DeviceObject);
    context = ImGetDeviceContext(device);
    count = (ULONG)(InputDataEnd - InputDataStart);
    transformed = FALSE;

    WdfSpinLockAcquire(context->MotionLock);

    for (index = 0; index < count; index++) {
        if (ImMotionTransformPacket(
            &context->Motion,
            &context->Config,
            &InputDataStart[index]
            )) {
            transformed = TRUE;
        }
    }

    WdfSpinLockRelease(context->MotionLock);

    (*(PSERVICE_CALLBACK_ROUTINE)context->UpperConnectData.ClassService)(
        context->UpperConnectData.ClassDeviceObject,
        InputDataStart,
        InputDataEnd,
        InputDataConsumed
        );

    if (transformed) {
        WdfTimerStart(
            context->FrictionTimer,
            WDF_REL_TIMEOUT_IN_MS(context->Config.TimerPeriodMs)
            );
    }
}

VOID
ImFrictionTimer(
    _In_ WDFTIMER Timer
    )
{
    PIM_DEVICE_CONTEXT context;
    CONNECT_DATA connectData;
    MOUSE_INPUT_DATA packet;
    WDFDEVICE device;
    ULONG consumed;
    BOOLEAN emit;
    BOOLEAN stop;

    device = WdfTimerGetParentObject(Timer);
    context = ImGetDeviceContext(device);
    consumed = 0;
    emit = FALSE;
    stop = FALSE;

    RtlZeroMemory(&packet, sizeof(packet));

    WdfSpinLockAcquire(context->MotionLock);

    connectData = context->UpperConnectData;

    if (connectData.ClassService != NULL) {
        emit = ImMotionBuildFrictionPacket(
            &context->Motion,
            &context->Config,
            &packet
            );

        stop =
            (context->Motion.VelocityXQ8 == 0) &&
            (context->Motion.VelocityYQ8 == 0) &&
            !emit;
    } else {
        stop = TRUE;
    }

    WdfSpinLockRelease(context->MotionLock);

    if (emit) {
        (*(PSERVICE_CALLBACK_ROUTINE)connectData.ClassService)(
            connectData.ClassDeviceObject,
            &packet,
            (&packet) + 1,
            &consumed
            );
    }

    if (stop) {
        WdfTimerStop(context->FrictionTimer, FALSE);
    }
}
