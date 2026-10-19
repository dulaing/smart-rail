import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';
import 'package:smartrail_core/smartrail_core.dart';

/// Measured Driver frame. SafeArea owns device insets; no illustrated system bars.
class DriverFrame extends StatelessWidget {
  const DriverFrame({
    super.key,
    required this.title,
    required this.child,
    this.displayName = 'Driver',
    this.selected = 'journeys',
    this.onSelect,
    this.now,
    this.connection,
    this.phoneAction,
    this.showReports = true,
    this.showJourneys = true,
    this.onPhoneBack,
  });

  final String title, displayName, selected;
  final Widget child;
  final ValueChanged<String>? onSelect;
  final DateTime? now;
  final Widget? connection, phoneAction;
  final bool showReports, showJourneys;
  final VoidCallback? onPhoneBack;

  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    return Scaffold(
      backgroundColor: p.bgCanvas,
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, box) {
            final wide =
                box.maxWidth >= 1000 &&
                MediaQuery.textScalerOf(context).scale(20) <= 28;
            final header = wide
                ? Container(
                    key: const Key('top-bar'),
                    height: 88,
                    color: p.bgSurface,
                    padding: const EdgeInsets.symmetric(horizontal: 32),
                    child: Row(
                      children: [
                        Expanded(
                          child: DriverText(
                            title,
                            style: driverText(context, 20, 28, secondary: true),
                          ),
                        ),
                        ?connection,
                        const SizedBox(width: 16),
                        if (now != null)
                          ConstrainedBox(
                            constraints: const BoxConstraints(minWidth: 56),
                            child: DriverText(
                              driverTime(now),
                              style: driverText(context, 28, 36),
                            ),
                          ),
                      ],
                    ),
                  )
                : Padding(
                    padding: const EdgeInsets.fromLTRB(55 / 3, 8, 55 / 3, 0),
                    child: Row(
                      children: [
                        if (onPhoneBack == null)
                          const DriverLogo(size: 32)
                        else
                          SizedBox.square(
                            dimension: 56,
                            child: IconButton(
                              tooltip: 'Back',
                              onPressed: onPhoneBack,
                              style: IconButton.styleFrom(
                                backgroundColor: p.bgSurface,
                              ),
                              icon: const DriverAsset('back', size: 24),
                            ),
                          ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: DriverText(
                            title,
                            style: driverText(
                              context,
                              22,
                              28,
                              weight: FontWeight.w600,
                            ),
                          ),
                        ),
                        Semantics(
                          button: true,
                          label: 'Account',
                          excludeSemantics: true,
                          child: SizedBox(
                            key: const Key('tab-account'),
                            width: 56,
                            height: 56,
                            child: Material(
                              color: p.bgSurface,
                              shape: const CircleBorder(),
                              child: InkWell(
                                customBorder: const CircleBorder(),
                                onTap: () => onSelect?.call('account'),
                                child: Center(
                                  child: DriverText(
                                    displayName.characters.first,
                                    style: driverText(
                                      context,
                                      20,
                                      28,
                                      weight: FontWeight.w500,
                                    ),
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
                  );
            final body = Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                header,
                Expanded(child: child),
                if (!wide && phoneAction != null)
                  Padding(
                    padding: const EdgeInsets.fromLTRB(
                      55 / 3,
                      16 / 3,
                      55 / 3,
                      20 / 3,
                    ),
                    child: phoneAction!,
                  ),
              ],
            );
            return wide
                ? Row(
                    children: [
                      SizedBox(
                        key: const Key('rail'),
                        width: 136,
                        child: _Rail(
                          displayName: displayName,
                          selected: selected,
                          onSelect: onSelect,
                          showReports: showReports,
                          showJourneys: showJourneys,
                        ),
                      ),
                      Expanded(child: body),
                    ],
                  )
                : body;
          },
        ),
      ),
    );
  }
}

TextStyle driverText(
  BuildContext context,
  double size,
  double line, {
  FontWeight weight = FontWeight.w400,
  bool secondary = false,
  Color? color,
}) {
  final theme = SrTheme.of(context);
  return TextStyle(
    inherit: false,
    fontFamily: 'packages/smartrail_core/${srFontFamilies['latin']}',
    fontFamilyFallback: [
      for (final script in ['sinhala', 'tamil'])
        'packages/smartrail_core/${srFontFamilies[script]}',
      'DriverSymbols',
    ],
    fontSize: size,
    height: line / size,
    fontWeight: weight,
    fontVariations: [FontVariation('wght', weight.value.toDouble())],
    color:
        color ??
        (secondary ? theme.palette.textSecondary : theme.palette.textPrimary),
    leadingDistribution: TextLeadingDistribution.even,
  );
}

String driverTime(DateTime? value) {
  if (value == null) return '—';
  final local = value.toUtc().add(const Duration(hours: 5, minutes: 30));
  return '${local.hour}:${local.minute.toString().padLeft(2, '0')}';
}

String driverDay(DateTime date) {
  const days = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  const months = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  return '${days[date.weekday - 1]} ${date.day} ${months[date.month - 1]}';
}

class DriverLogo extends StatelessWidget {
  const DriverLogo({super.key, required this.size});
  final double size;
  @override
  Widget build(BuildContext context) => Image.asset(
    'assets/figma/driver-logo.png',
    width: size,
    height: size,
    fit: BoxFit.contain,
    excludeFromSemantics: true,
  );
}

class DriverAsset extends StatelessWidget {
  const DriverAsset(this.name, {super.key, required this.size, this.color});
  final String name;
  final double size;
  final Color? color;
  @override
  Widget build(BuildContext context) => SizedBox.square(
    dimension: size,
    child: SvgPicture.asset(
      'assets/figma/$name.svg',
      fit: BoxFit.contain,
      colorFilter: color == null
          ? null
          : ColorFilter.mode(color!, BlendMode.srcIn),
      excludeFromSemantics: true,
    ),
  );
}

class _Rail extends StatelessWidget {
  const _Rail({
    required this.displayName,
    required this.selected,
    this.onSelect,
    required this.showReports,
    required this.showJourneys,
  });
  final String displayName, selected;
  final ValueChanged<String>? onSelect;
  final bool showReports, showJourneys;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    return ColoredBox(
      color: p.bgSurface,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 24, 12, 16),
        child: Column(
          children: [
            const DriverLogo(size: 48),
            const SizedBox(height: 16),
            DriverText(
              'DRIVER',
              style: driverText(
                context,
                14,
                20,
                weight: FontWeight.w600,
                secondary: true,
              ),
            ),
            const SizedBox(height: 16),
            for (final item in [
              if (showJourneys) ('journeys', 'Briefing', 'briefing'),
              if (showJourneys) ('updates', 'Updates', 'updates'),
              if (showReports) ('reports', 'Reports', 'reports'),
            ]) ...[
              Semantics(
                button: true,
                selected: selected == item.$1,
                label: item.$2,
                excludeSemantics: true,
                child: SizedBox(
                  key: Key('tab-${item.$1}'),
                  width: 112,
                  height: 112,
                  child: Material(
                    borderRadius: BorderRadius.circular(28),
                    color: selected == item.$1 ? p.navActive : p.bgSurface,
                    child: InkWell(
                      borderRadius: BorderRadius.circular(28),
                      onTap: () => onSelect?.call(item.$1),
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          DriverAsset(
                            item.$3,
                            size: 32,
                            color: selected == item.$1
                                ? p.navActiveInk
                                : p.textSecondary,
                          ),
                          const SizedBox(height: 8),
                          DriverText(
                            item.$2,
                            style: driverText(
                              context,
                              18,
                              24,
                              weight: FontWeight.w600,
                              color: selected == item.$1
                                  ? p.navActiveInk
                                  : p.textSecondary,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
              const SizedBox(height: 8),
            ],
            const Spacer(),
            SizedBox(
              key: const Key('tab-account'),
              width: 112,
              height: 96,
              child: Semantics(
                button: true,
                label: 'Account',
                excludeSemantics: true,
                child: InkWell(
                  onTap: () => onSelect?.call('account'),
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      CircleAvatar(
                        radius: 22,
                        backgroundColor: p.bgSurface2,
                        child: DriverText(
                          displayName.characters.first,
                          style: driverText(
                            context,
                            20,
                            28,
                            weight: FontWeight.w500,
                          ),
                        ),
                      ),
                      const SizedBox(height: 4),
                      DriverText(
                        displayName,
                        style: driverText(context, 16, 22, secondary: true),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The top-bar status: simulated-data chip and the server-check state beside the clock.
class DriverConnection extends StatelessWidget {
  const DriverConnection({
    super.key,
    required this.label,
    this.semanticsLabel,
    this.showDot = true,
    this.warning = false,
  });
  final String label;
  final String? semanticsLabel;
  final bool showDot, warning;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    return Row(
      children: [
        Container(
          constraints: const BoxConstraints(minWidth: 141.5),
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
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
        const SizedBox(width: 16),
        SizedBox(
          width: 180,
          child: Semantics(
            label: semanticsLabel ?? label,
            child: Row(
              children: [
                if (showDot) ...[
                  Container(
                    width: 8,
                    height: 8,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      color: warning ? p.statusWarning : p.statusSuccess,
                    ),
                  ),
                  const SizedBox(width: 8),
                ],
                Expanded(
                  child: DriverText(
                    label,
                    style: driverText(
                      context,
                      16,
                      22,
                      color: warning ? p.statusWarningInk : p.textSecondary,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

class DriverAction extends StatelessWidget {
  const DriverAction({
    super.key,
    required this.label,
    required this.onPressed,
    this.phone = false,
    this.outlined = false,
    this.bordered = false,
  });
  final String label;
  final VoidCallback? onPressed;
  final bool phone, outlined, bordered;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    return FilledButton(
      style: FilledButton.styleFrom(
        backgroundColor: outlined ? p.bgSurface : p.navActive,
        foregroundColor: outlined ? p.textPrimary : p.navActiveInk,
        side: outlined && (!phone || bordered)
            ? BorderSide(color: p.lineHairline)
            : null,
        minimumSize: Size(double.infinity, phone ? 64 : 96),
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(phone ? 44 / 3 : 28),
        ),
        textStyle: driverText(context, 22, 28, weight: FontWeight.w500),
      ),
      onPressed: onPressed,
      child: DriverText(label),
    );
  }
}

/// Keep Figma's explicit line boxes when a symbol uses a fallback font with taller metrics.
class DriverText extends Text {
  DriverText(
    super.data, {
    super.key,
    super.style,
    super.semanticsLabel,
    super.textAlign,
  }) : super(
         strutStyle: style == null
             ? null
             : StrutStyle.fromTextStyle(style, forceStrutHeight: true),
       );
}
