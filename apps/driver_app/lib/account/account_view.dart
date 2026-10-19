import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../driver_frame.dart';

/// Account presentation only. Sign-in state and navigation stay in the shell and controller.
/// The account read supplies a display name and role codes, so nothing else is shown about the person.
class DriverAccountView extends StatelessWidget {
  const DriverAccountView({
    super.key,
    required this.displayName,
    required this.roles,
    required this.onSignOut,
    required this.onSelect,
    this.noAccessMessage,
    this.offline = false,
    this.busy = false,
    this.now,
    this.checkedAt,
    this.showJourneys = true,
    this.showReports = true,
    this.statusSample,
  });

  final String displayName;
  final List<String> roles;

  /// Set when the account holds no driver permission: it can only sign out.
  final String? noAccessMessage;
  final bool offline, busy, showJourneys, showReports;
  final DateTime? now, checkedAt;
  final VoidCallback onSignOut;
  final ValueChanged<String> onSelect;

  /// A debug-only entry; null in release builds.
  final VoidCallback? statusSample;

  String get _roles =>
      roles.isEmpty ? 'No role assigned' : roles.map(_roleName).join(', ');

  @override
  Widget build(BuildContext context) {
    final wide =
        MediaQuery.sizeOf(context).width >= 1000 &&
        MediaQuery.textScalerOf(context).scale(20) <= 28;
    final signOut = offline ? 'Reconnect and sign in' : 'Sign out';
    final action = DriverAction(
      key: const Key('account-signout'),
      label: signOut,
      phone: !wide,
      outlined: !wide,
      onPressed: busy ? null : onSignOut,
    );
    return DriverFrame(
      title: 'Staff account',
      displayName: displayName,
      selected: 'account',
      now: now,
      showJourneys: showJourneys,
      showReports: showReports,
      connection: wide
          ? DriverConnection(
              label: offline
                  ? 'Offline'
                  : checkedAt == null
                  ? 'Not checked'
                  : 'Live · ${driverTime(checkedAt)}',
              showDot: !offline && checkedAt != null,
            )
          : null,
      onPhoneBack: showJourneys ? () => onSelect('journeys') : null,
      onSelect: onSelect,
      phoneAction: wide ? null : action,
      child: SingleChildScrollView(
        key: const Key('account-scroll'),
        padding: EdgeInsets.fromLTRB(
          wide ? 32 : 55 / 3,
          wide ? 24 : 16,
          wide ? 32 : 55 / 3,
          wide ? 10 : 16,
        ),
        child: wide ? _tablet(context, action) : _phone(context),
      ),
    );
  }

  Widget _tablet(BuildContext context, Widget action) {
    final p = SrTheme.of(context).palette;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Container(
          key: const Key('account-card'),
          padding: const EdgeInsets.all(32),
          decoration: BoxDecoration(
            color: p.bgSurface,
            borderRadius: BorderRadius.circular(28),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                key: const Key('account-badge'),
                padding: const EdgeInsets.symmetric(
                  horizontal: 12,
                  vertical: 8,
                ),
                decoration: BoxDecoration(
                  color: p.accentSoft,
                  borderRadius: BorderRadius.circular(999),
                ),
                child: DriverText(
                  'Signed-in staff',
                  style: driverText(context, 14, 20, weight: FontWeight.w600),
                ),
              ),
              const SizedBox(height: 16),
              SizedBox(
                key: const Key('account-name'),
                width: double.infinity,
                child: DriverText(
                  displayName,
                  style: driverText(context, 36, 44, weight: FontWeight.w600),
                ),
              ),
              const SizedBox(height: 16),
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: _Field(
                      key: const Key('account-role'),
                      label: 'ROLE',
                      value: _roles,
                    ),
                  ),
                  const SizedBox(width: 24),
                  Expanded(
                    child: showJourneys
                        ? const _Field(
                            key: Key('account-briefings'),
                            label: 'BRIEFINGS',
                            value: 'Open briefings to choose a journey.',
                          )
                        : const SizedBox.shrink(),
                  ),
                ],
              ),
              if (noAccessMessage != null) ...[
                const SizedBox(height: 16),
                _NoAccess(noAccessMessage!),
              ],
              const SizedBox(height: 16),
              DriverText(
                key: const Key('account-note'),
                'Sign out when handing over this device.',
                style: driverText(context, 16, 22, secondary: true),
              ),
            ],
          ),
        ),
        const SizedBox(height: 24),
        Row(
          key: const Key('account-actions'),
          children: [
            Expanded(child: action),
            const SizedBox(width: 24),
            Expanded(
              child: showJourneys
                  ? DriverAction(
                      key: const Key('account-open-briefings'),
                      label: 'Open briefings',
                      outlined: true,
                      onPressed: () => onSelect('journeys'),
                    )
                  : const SizedBox.shrink(),
            ),
          ],
        ),
        if (statusSample != null)
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton(
              onPressed: statusSample,
              child: const Text('Status sample'),
            ),
          ),
      ],
    );
  }

  Widget _phone(BuildContext context) {
    final p = SrTheme.of(context).palette;
    final gap = 32 / 3;
    final radius = BorderRadius.circular(44 / 3);
    Widget card(Key key, List<Widget> children, {double pad = 40 / 3}) =>
        Container(
          key: key,
          padding: EdgeInsets.all(pad),
          decoration: BoxDecoration(color: p.bgSurface, borderRadius: radius),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: children,
          ),
        );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        card(const Key('account-profile'), [
          const Align(
            alignment: Alignment.centerLeft,
            child: DriverAsset(
              'phone-user',
              size: 48,
              key: Key('account-icon'),
            ),
          ),
          const SizedBox(height: 8),
          DriverText(
            displayName,
            key: const Key('account-name'),
            style: driverText(context, 28, 36, weight: FontWeight.w600),
          ),
          const SizedBox(height: 8),
          DriverText(
            _roles,
            key: const Key('account-role'),
            style: driverText(
              context,
              22,
              28,
              weight: FontWeight.w600,
              secondary: true,
            ),
          ),
        ], pad: 16),
        if (noAccessMessage != null) ...[
          SizedBox(height: gap),
          _NoAccess(noAccessMessage!),
        ],
        if (showJourneys || showReports) ...[
          SizedBox(height: gap),
          card(const Key('account-briefings'), [
            DriverText(
              showJourneys
                  ? showReports
                        ? 'Briefings and reports'
                        : 'Briefings'
                  : 'Reports',
              key: const Key('account-briefings-title'),
              style: driverText(context, 22, 28, weight: FontWeight.w600),
            ),
            const SizedBox(height: 8),
            DriverText(
              !showJourneys
                  ? 'Choose a journey to open its reports.'
                  : showReports
                  ? 'Choose a journey to open its briefing, updates or reports.'
                  : 'Choose a journey to open its briefing or updates.',
              key: const Key('account-briefings-copy'),
              style: driverText(context, 16, 22, secondary: true),
            ),
            if (showJourneys) ...[
              const SizedBox(height: 8),
              DriverAction(
                key: const Key('account-open-briefings'),
                label: 'Open briefings',
                phone: true,
                outlined: true,
                bordered: true,
                onPressed: busy ? null : () => onSelect('journeys'),
              ),
              const SizedBox(height: 8),
              DriverAction(
                key: const Key('account-open-updates'),
                label: 'Open updates',
                phone: true,
                outlined: true,
                bordered: true,
                onPressed: busy ? null : () => onSelect('updates'),
              ),
            ],
            if (showReports) ...[
              const SizedBox(height: 8),
              DriverAction(
                key: const Key('account-open-reports'),
                label: 'Open reports',
                phone: true,
                outlined: true,
                bordered: true,
                onPressed: offline || busy ? null : () => onSelect('reports'),
              ),
              if (offline) ...[
                const SizedBox(height: 8),
                DriverText(
                  'Connect to open reports',
                  style: driverText(context, 16, 22, secondary: true),
                ),
              ],
            ],
          ]),
        ],
        SizedBox(height: gap),
        card(const Key('account-signin'), [
          const Align(
            alignment: Alignment.centerLeft,
            child: DriverAsset(
              'phone-shield',
              size: 32,
              key: Key('account-shield'),
            ),
          ),
          const SizedBox(height: 8),
          DriverText(
            'Staff sign-in service',
            style: driverText(context, 22, 28, weight: FontWeight.w600),
          ),
          const SizedBox(height: 8),
          DriverText(
            'Your staff account manages sign-in.',
            style: driverText(context, 20, 28, secondary: true),
          ),
        ]),
        if (statusSample != null)
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton(
              onPressed: statusSample,
              child: const Text('Status sample'),
            ),
          ),
      ],
    );
  }
}

class _Field extends StatelessWidget {
  const _Field({super.key, required this.label, required this.value});
  final String label, value;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      DriverText(
        label,
        style: driverText(
          context,
          14,
          20,
          weight: FontWeight.w600,
          secondary: true,
        ),
      ),
      const SizedBox(height: 8),
      DriverText(
        value,
        style: driverText(context, 20, 28, weight: FontWeight.w500),
      ),
    ],
  );
}

class _NoAccess extends StatelessWidget {
  const _NoAccess(this.message);
  final String message;
  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    child: DriverText(
      message,
      key: const Key('no-driver-access'),
      style: driverText(
        context,
        20,
        28,
        color: SrTheme.of(context).palette.statusDangerInk,
      ),
    ),
  );
}

/// Role codes arrive as DRIVER or SYS_ADMIN; people read them as words.
String _roleName(String code) {
  final words = code.toLowerCase().replaceAll('_', ' ');
  return words.isEmpty ? code : words[0].toUpperCase() + words.substring(1);
}
