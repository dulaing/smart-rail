import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'driver_frame.dart';
import 'journeys/driver_rows.dart';

/// Which signed-out screen to show. These come from the existing session and account checks; the
/// view only presents them.
enum GateKind { checking, signedOut, expired, missing, failed }

/// Signed-out presentation for tablet and phone. Sign-in, retry and sign-out behaviour stay in the
/// gate and its controller.
class DriverGateView extends StatelessWidget {
  const DriverGateView({
    super.key,
    required this.kind,
    this.message,
    this.signInFailed = false,
    this.signInFailedMessage,
    this.busy = false,
    this.onSignIn,
    this.onSignOut,
    this.onRetry,
  });

  final GateKind kind;

  /// The account or connection message for [GateKind.missing] and [GateKind.failed].
  final String? message;
  final bool signInFailed, busy;
  final String? signInFailedMessage;
  final VoidCallback? onSignIn, onSignOut, onRetry;

  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    final wide =
        MediaQuery.sizeOf(context).width >= 1000 &&
        MediaQuery.textScalerOf(context).scale(20) <= 28;
    final notice = signInFailed && signInFailedMessage != null
        ? Semantics(
            liveRegion: true,
            child: DriverText(
              signInFailedMessage!,
              key: const Key('notice'),
              style: driverText(context, 20, 28, color: p.statusDangerInk),
            ),
          )
        : null;
    final body = switch (kind) {
      GateKind.signedOut =>
        wide
            ? _signedOutTablet(context, notice)
            : _signedOutPhone(context, notice),
      _ => _statePanel(context, wide, notice),
    };
    final phoneAction = wide ? null : _phoneAction(context);
    return Scaffold(
      backgroundColor: p.bgCanvas,
      body: SafeArea(
        child: Column(
          children: [
            Expanded(
              child: SingleChildScrollView(
                key: const Key('gate-scroll'),
                padding: wide
                    ? const EdgeInsets.fromLTRB(48, 32, 48, 32)
                    : const EdgeInsets.fromLTRB(55 / 3, 8, 55 / 3, 16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    wide ? _tabletHeader(context) : _phoneHeader(context),
                    SizedBox(height: wide ? 80 : 16),
                    if (wide)
                      Align(
                        alignment: Alignment.topCenter,
                        child: ConstrainedBox(
                          constraints: const BoxConstraints(maxWidth: 1080),
                          child: body,
                        ),
                      )
                    else
                      body,
                  ],
                ),
              ),
            ),
            if (phoneAction != null)
              Padding(
                padding: const EdgeInsets.fromLTRB(
                  55 / 3,
                  16 / 3,
                  55 / 3,
                  20 / 3,
                ),
                child: phoneAction,
              ),
          ],
        ),
      ),
    );
  }

  Widget _tabletHeader(BuildContext context) => SizedBox(
    key: const Key('gate-header'),
    height: 48,
    child: Row(
      children: [
        const DriverLogo(size: 48),
        const SizedBox(width: 12),
        Expanded(
          child: Semantics(
            header: true,
            child: DriverText(
              'SmartRail Driver',
              key: const Key('gate-brand'),
              style: driverText(context, 28, 36, weight: FontWeight.w600),
            ),
          ),
        ),
        const _SimulatedChip(wide: true),
      ],
    ),
  );

  Widget _phoneHeader(BuildContext context) => ConstrainedBox(
    key: const Key('gate-header'),
    constraints: const BoxConstraints(minHeight: 56),
    child: Row(
      children: [
        const DriverLogo(size: 32),
        const SizedBox(width: 8),
        Expanded(
          child: Semantics(
            header: true,
            child: DriverText(
              'SmartRail Driver',
              key: const Key('gate-brand'),
              style: driverText(context, 22, 28, weight: FontWeight.w600),
            ),
          ),
        ),
      ],
    ),
  );

  Widget? _phoneAction(BuildContext context) => switch (kind) {
    GateKind.checking => null,
    GateKind.signedOut => DriverAction(
      key: const Key('gate-primary'),
      label: 'Continue to staff sign-in',
      phone: true,
      onPressed: busy ? null : onSignIn,
    ),
    GateKind.expired => DriverAction(
      key: const Key('gate-primary'),
      label: 'Sign in again',
      phone: true,
      onPressed: busy ? null : onSignIn,
    ),
    GateKind.failed => DriverAction(
      key: const Key('gate-primary'),
      label: 'Try again',
      phone: true,
      onPressed: busy ? null : onRetry,
    ),
    GateKind.missing => DriverAction(
      key: const Key('gate-primary'),
      label: 'Sign out',
      phone: true,
      outlined: true,
      onPressed: busy ? null : onSignOut,
    ),
  };

  Widget _signedOutTablet(BuildContext context, Widget? notice) {
    final p = SrTheme.of(context).palette;
    final intro = SizedBox(
      key: const Key('gate-intro'),
      width: 540,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            decoration: BoxDecoration(
              color: p.accentSoft,
              borderRadius: BorderRadius.circular(999),
            ),
            child: DriverText(
              'DRIVER · CAB TABLET',
              style: driverText(context, 14, 20, weight: FontWeight.w600),
            ),
          ),
          const SizedBox(height: 24),
          DriverText(
            'Your duty,\nclearly laid out.',
            style: driverText(context, 36, 44, weight: FontWeight.w600),
          ),
          const SizedBox(height: 24),
          SizedBox(
            width: 500,
            child: DriverText(
              'Assigned train, confirmed changes and clear report status.',
              style: driverText(context, 20, 28, secondary: true),
            ),
          ),
          const SizedBox(height: 24),
          const _RouteCard(),
        ],
      ),
    );
    final card = Container(
      key: const Key('gate-card'),
      width: 500,
      padding: const EdgeInsets.all(32),
      decoration: BoxDecoration(
        color: p.bgSurface,
        borderRadius: BorderRadius.circular(28),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Semantics(
            header: true,
            child: DriverText(
              'Staff sign-in',
              style: driverText(context, 28, 36, weight: FontWeight.w600),
            ),
          ),
          const SizedBox(height: 24),
          DriverText(
            'Continue with your assigned staff account.',
            style: driverText(context, 20, 28, secondary: true),
          ),
          if (notice != null) ...[const SizedBox(height: 24), notice],
          const SizedBox(height: 24),
          DriverAction(
            key: const Key('gate-primary'),
            label: 'Sign in with staff account',
            onPressed: busy ? null : onSignIn,
          ),
          const SizedBox(height: 24),
          DriverText(
            'Sign-in opens your organisation’s secure sign-in page.',
            style: driverText(context, 16, 22, secondary: true),
          ),
        ],
      ),
    );
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [intro, const SizedBox(width: 40), card],
    );
  }

  Widget _signedOutPhone(BuildContext context, Widget? notice) {
    final p = SrTheme.of(context).palette;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const _SimulatedChip(wide: false),
        const SizedBox(height: 16),
        const Align(
          alignment: Alignment.centerLeft,
          child: DriverAsset('phone-train', size: 72, key: Key('gate-icon')),
        ),
        const SizedBox(height: 16),
        DriverText(
          'Ready for duty?',
          key: const Key('gate-title'),
          style: driverText(context, 28, 36, weight: FontWeight.w600),
        ),
        const SizedBox(height: 16),
        DriverText(
          'Your assigned train, confirmed updates and reports to Control.',
          style: driverText(context, 20, 28, secondary: true),
        ),
        const SizedBox(height: 16),
        Container(
          key: const Key('gate-card'),
          padding: const EdgeInsets.all(40 / 3),
          decoration: BoxDecoration(
            color: p.bgSurface,
            borderRadius: BorderRadius.circular(44 / 3),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Align(
                alignment: Alignment.centerLeft,
                child: DriverAsset('phone-shield', size: 32),
              ),
              const SizedBox(height: 8),
              DriverText(
                'Use your staff account',
                style: driverText(context, 22, 28, weight: FontWeight.w600),
              ),
              const SizedBox(height: 8),
              DriverText(
                'Continue to the staff sign-in service.',
                style: driverText(context, 20, 28, secondary: true),
              ),
            ],
          ),
        ),
        if (notice != null) ...[const SizedBox(height: 16), notice],
        const SizedBox(height: 16),
        DriverText(
          'SmartRail does not collect your password.',
          key: const Key('gate-note'),
          style: driverText(context, 16, 22, secondary: true),
        ),
      ],
    );
  }

  Widget _statePanel(BuildContext context, bool wide, Widget? notice) {
    final (title, copy, primary, secondary) = switch (kind) {
      GateKind.checking => (
        'Checking your session…',
        'Protected staff content stays hidden until your access is confirmed.',
        null,
        null,
      ),
      GateKind.expired => (
        'Your session has expired',
        'Sign in again to view your assigned duty and reports.',
        'Sign in again',
        null,
      ),
      GateKind.missing => (
        'No active SmartRail account',
        message ?? '',
        null,
        'Sign out',
      ),
      GateKind.failed => (
        'Could not reach SmartRail',
        message ?? '',
        'Try again',
        'Sign out',
      ),
      GateKind.signedOut => ('', '', null, null),
    };
    final action = switch (kind) {
      GateKind.expired => onSignIn,
      GateKind.failed => onRetry,
      _ => null,
    };
    final panel = DriverStatePanel(
      key: Key('gate-${kind.name}'),
      panelKey: 'gate-panel',
      prefix: 'gate',
      wide: wide,
      icon: wide ? 'shield' : 'phone-shield',
      title: title,
      copy: copy,
      actionLabel: wide ? primary : null,
      onAction: busy ? null : action,
      secondaryLabel: wide ? secondary : null,
      onSecondary: busy ? null : onSignOut,
    );
    return Semantics(
      liveRegion: kind == GateKind.checking,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (!wide) const _SimulatedChip(wide: false),
          if (!wide) const SizedBox(height: 16),
          panel,
          if (notice != null) ...[const SizedBox(height: 16), notice],
          if (!wide && kind == GateKind.failed) ...[
            const SizedBox(height: 16),
            DriverAction(
              key: const Key('gate-secondary'),
              label: 'Sign out',
              phone: true,
              outlined: true,
              onPressed: busy ? null : onSignOut,
            ),
          ],
        ],
      ),
    );
  }
}

class _SimulatedChip extends StatelessWidget {
  const _SimulatedChip({required this.wide});
  final bool wide;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    return Align(
      alignment: wide ? Alignment.centerRight : Alignment.centerLeft,
      widthFactor: 1,
      child: Container(
        key: const Key('gate-chip'),
        constraints: BoxConstraints(minWidth: wide ? 141.5 : 0),
        padding: EdgeInsets.all(wide ? 12 : 16 / 3)
            .copyWith(top: wide ? 8 : 16 / 3, bottom: wide ? 8 : 16 / 3),
        decoration: BoxDecoration(
          color: p.bgSurface2,
          borderRadius: BorderRadius.circular(999),
        ),
        child: DriverText(
          'SIMULATED DATA',
          style: driverText(
            context,
            14,
            20,
            weight: FontWeight.w600,
            secondary: true,
          ),
        ),
      ),
    );
  }
}

class _RouteCard extends StatelessWidget {
  const _RouteCard();
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    Widget label(String text) => SizedBox(
      width: 156,
      child: DriverText(
        text,
        style: driverText(context, 16, 22, secondary: true),
      ),
    );
    return Container(
      key: const Key('gate-route'),
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        color: p.bgSurface,
        borderRadius: BorderRadius.circular(28),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            key: const Key('gate-route-art'),
            height: 16,
            child: SvgPicture.asset(
              'assets/figma/sign-in-route.svg',
              fit: BoxFit.contain,
              excludeFromSemantics: true,
            ),
          ),
          const SizedBox(height: 20),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              label('Veyangoda'),
              label('Gampaha'),
              label('Colombo Fort'),
            ],
          ),
          const SizedBox(height: 20),
          DriverText(
            'Station progress · not exact GPS',
            style: driverText(
              context,
              14,
              20,
              weight: FontWeight.w600,
              secondary: true,
            ),
          ),
        ],
      ),
    );
  }
}
