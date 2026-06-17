/*++
Module Name:

    config.c

Abstract:

    Registry-backed configuration for the inertial mouse filter.

--*/

#include "config.h"

static
ULONG
ImClampUlong(
    _In_ ULONG Value,
    _In_ ULONG Minimum,
    _In_ ULONG Maximum
    )
{
    if (Value < Minimum) {
        return Minimum;
    }

    if (Value > Maximum) {
        return Maximum;
    }

    return Value;
}

static
VOID
ImQueryULong(
    _In_ WDFKEY Key,
    _In_ PCWSTR NameText,
    _Inout_ PULONG Value
    )
{
    UNICODE_STRING name;
    ULONG registryValue;

    RtlInitUnicodeString(&name, NameText);

    if (NT_SUCCESS(WdfRegistryQueryULong(Key, &name, &registryValue))) {
        *Value = registryValue;
    }
}

static
VOID
ImConfigReadValues(
    _In_ WDFKEY Key,
    _Inout_ PIM_CONFIG Config
    )
{
    ULONG value;

    value = Config->Enabled ? 1 : 0;
    ImQueryULong(Key, L"InertialEnabled", &value);
    Config->Enabled = (value != 0) ? TRUE : FALSE;

    value = Config->TimerPeriodMs;
    ImQueryULong(Key, L"TimerPeriodMs", &value);
    Config->TimerPeriodMs = ImClampUlong(value, IM_MIN_TIMER_MS, IM_MAX_TIMER_MS);

    value = (ULONG)Config->AccelXQ8;
    ImQueryULong(Key, L"AccelXQ8", &value);
    Config->AccelXQ8 = (LONG)ImClampUlong(value, IM_MIN_Q8, IM_MAX_Q8);

    value = (ULONG)Config->AccelYQ8;
    ImQueryULong(Key, L"AccelYQ8", &value);
    Config->AccelYQ8 = (LONG)ImClampUlong(value, IM_MIN_Q8, IM_MAX_Q8);

    value = (ULONG)Config->FrictionXQ8;
    ImQueryULong(Key, L"FrictionXQ8", &value);
    Config->FrictionXQ8 = (LONG)ImClampUlong(value, IM_MIN_Q8, IM_MAX_Q8);

    value = (ULONG)Config->FrictionYQ8;
    ImQueryULong(Key, L"FrictionYQ8", &value);
    Config->FrictionYQ8 = (LONG)ImClampUlong(value, IM_MIN_Q8, IM_MAX_Q8);

    value = (ULONG)Config->MaxVelocityXQ8;
    ImQueryULong(Key, L"MaxVelocityXQ8", &value);
    Config->MaxVelocityXQ8 = (LONG)ImClampUlong(value, IM_MIN_Q8, IM_MAX_Q8);

    value = (ULONG)Config->MaxVelocityYQ8;
    ImQueryULong(Key, L"MaxVelocityYQ8", &value);
    Config->MaxVelocityYQ8 = (LONG)ImClampUlong(value, IM_MIN_Q8, IM_MAX_Q8);
}

static
VOID
ImConfigDefaults(
    _Out_ PIM_CONFIG Config
    )
{
    Config->Enabled = IM_DEFAULT_ENABLED;
    Config->TimerPeriodMs = IM_DEFAULT_TIMER_MS;
    Config->AccelXQ8 = IM_DEFAULT_ACCEL_X_Q8;
    Config->AccelYQ8 = IM_DEFAULT_ACCEL_Y_Q8;
    Config->FrictionXQ8 = IM_DEFAULT_FRICTION_X_Q8;
    Config->FrictionYQ8 = IM_DEFAULT_FRICTION_Y_Q8;
    Config->MaxVelocityXQ8 = IM_DEFAULT_MAX_VEL_X_Q8;
    Config->MaxVelocityYQ8 = IM_DEFAULT_MAX_VEL_Y_Q8;
}

VOID
ImConfigLoad(
    _In_ WDFDEVICE Device,
    _Out_ PIM_CONFIG Config
    )
{
    WDFKEY deviceKey;
    WDFKEY parametersKey;
    UNICODE_STRING parametersName;
    NTSTATUS status;

    ImConfigDefaults(Config);

    status = WdfDeviceOpenRegistryKey(
        Device,
        PLUGPLAY_REGKEY_DEVICE,
        KEY_READ,
        WDF_NO_OBJECT_ATTRIBUTES,
        &deviceKey
        );

    if (!NT_SUCCESS(status)) {
        return;
    }

    RtlInitUnicodeString(&parametersName, L"Device Parameters");
    status = WdfRegistryOpenKey(
        deviceKey,
        &parametersName,
        KEY_READ,
        WDF_NO_OBJECT_ATTRIBUTES,
        &parametersKey
        );

    if (NT_SUCCESS(status)) {
        ImConfigReadValues(parametersKey, Config);
        WdfRegistryClose(parametersKey);
    } else {
        ImConfigReadValues(deviceKey, Config);
    }

    WdfRegistryClose(deviceKey);
}
