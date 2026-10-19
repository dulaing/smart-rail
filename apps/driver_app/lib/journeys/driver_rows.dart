import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../driver_frame.dart';

enum RowTone { neutral, accent, success, warning, danger }

/// The approved Reports-list card, reused for journeys and reports. Tablet and phone share one
/// anatomy: icon box, title and copy, then a status chip and an optional detail line.
class DriverRow extends StatelessWidget {
  const DriverRow({
    super.key,
    required this.id,
    required this.rowKey,
    required this.title,
    required this.copy,
    required this.semantics,
    this.chip,
    this.detail,
    this.tone = RowTone.neutral,
    this.icon = 'reports',
    this.wide = true,
    this.onTap,
  });

  /// Suffix of the keys of this row's parts, e.g. `row-chip-$id`.
  final String id;
  final Key rowKey;
  final String title, copy, semantics;
  final String? chip, detail;
  final RowTone tone;

  /// `reports` (flag) or `briefing` (list) icon asset.
  final String icon;
  final bool wide;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    final (soft, iconInk, chipFill, chipInk) = switch (tone) {
      RowTone.danger => (
        p.statusDangerSoft,
        p.statusDanger,
        p.statusDangerSoft,
        p.statusDangerInk,
      ),
      RowTone.success => (
        p.statusSuccessSoft,
        p.statusSuccess,
        p.statusSuccessSoft,
        p.statusSuccessInk,
      ),
      RowTone.warning => (
        p.statusWarningSoft,
        p.statusWarning,
        p.statusWarningSoft,
        p.statusWarningInk,
      ),
      RowTone.accent => (
        p.accentSoft,
        p.accentPrimary,
        p.accentSoft,
        p.textPrimary,
      ),
      RowTone.neutral => (
        p.accentSoft,
        p.accentPrimary,
        p.bgSurface2,
        p.textSecondary,
      ),
    };
    final radius = BorderRadius.circular(wide ? 28 : 44 / 3);
    final box = wide ? 64.0 : 48.0;
    final chipWidget = chip == null
        ? null
        : Container(
            key: Key('row-chip-$id'),
            padding: EdgeInsets.symmetric(
              horizontal: wide ? 12 : 8,
              vertical: wide ? 8 : 4,
            ),
            decoration: BoxDecoration(
              color: chipFill,
              borderRadius: BorderRadius.circular(999),
            ),
            child: DriverText(
              chip!,
              style: driverText(
                context,
                14,
                20,
                weight: FontWeight.w600,
                color: chipInk,
              ),
            ),
          );
    final detailWidget = detail == null
        ? null
        : DriverText(
            detail!,
            key: Key('row-ack-$id'),
            style: driverText(context, 16, 22, secondary: true),
          );
    final iconBox = Container(
      key: Key('row-icon-$id'),
      width: box,
      height: box,
      alignment: Alignment.center,
      decoration: BoxDecoration(color: soft, borderRadius: radius),
      child: DriverAsset(icon, size: wide ? 32 : 24, color: iconInk),
    );
    final titleWidget = DriverText(
      title,
      key: Key('row-title-$id'),
      style: driverText(context, 20, 28, weight: FontWeight.w500),
    );
    final copyWidget = DriverText(
      copy,
      key: Key('row-copy-$id'),
      style: driverText(context, 16, 22, secondary: true),
    );
    return Semantics(
      button: onTap != null,
      label: semantics,
      excludeSemantics: true,
      child: ConstrainedBox(
        constraints: BoxConstraints(minHeight: wide ? 112 : 72),
        child: Material(
          key: rowKey,
          color: p.bgSurface,
          borderRadius: radius,
          child: InkWell(
            borderRadius: radius,
            onTap: onTap,
            child: Padding(
              padding: wide
                  ? const EdgeInsets.symmetric(horizontal: 24, vertical: 12)
                  : const EdgeInsets.all(40 / 3),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.center,
                children: [
                  iconBox,
                  const SizedBox(width: 8),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        titleWidget,
                        const SizedBox(height: 4),
                        copyWidget,
                        if (!wide && chipWidget != null) ...[
                          const SizedBox(height: 4),
                          Align(
                            alignment: Alignment.centerLeft,
                            child: chipWidget,
                          ),
                        ],
                        if (!wide && detailWidget != null) ...[
                          const SizedBox(height: 4),
                          detailWidget,
                        ],
                      ],
                    ),
                  ),
                  if (wide && (chipWidget != null || detailWidget != null)) ...[
                    const SizedBox(width: 8),
                    SizedBox(
                      key: Key('row-delivery-$id'),
                      width: 280,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          ?chipWidget,
                          if (detailWidget != null) ...[
                            const SizedBox(height: 8),
                            detailWidget,
                          ],
                        ],
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Service-date control: previous day, the date, next day.
class DriverDateControl extends StatelessWidget {
  const DriverDateControl({
    super.key,
    required this.label,
    required this.onPrevious,
    required this.onNext,
    this.wide = true,
  });
  final String label;
  final VoidCallback onPrevious, onNext;
  final bool wide;

  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    Widget button(Key key, String name, String asset, VoidCallback onTap) =>
        Semantics(
          button: true,
          label: name,
          excludeSemantics: true,
          child: SizedBox.square(
            key: key,
            dimension: 56,
            child: Material(
              color: p.bgSurface,
              shape: const CircleBorder(),
              child: InkWell(
                customBorder: const CircleBorder(),
                onTap: onTap,
                child: Center(
                  child: DriverAsset(
                    asset,
                    size: wide ? 28 : 24,
                    color: p.textPrimary,
                  ),
                ),
              ),
            ),
          ),
        );
    final text = DriverText(
      label,
      key: const Key('date-label'),
      textAlign: TextAlign.center,
      style: driverText(context, 20, 28, weight: FontWeight.w500),
    );
    return Row(
      key: const Key('selector-date'),
      mainAxisSize: wide ? MainAxisSize.min : MainAxisSize.max,
      children: [
        button(const Key('date-previous'), 'Previous day', 'back', onPrevious),
        const SizedBox(width: 8),
        if (wide) SizedBox(width: 168, child: text) else Expanded(child: text),
        const SizedBox(width: 8),
        button(const Key('date-next'), 'Next day', 'chevron', onNext),
      ],
    );
  }
}

/// A section title with an optional note, as in the Reports chooser.
class DriverSection extends StatelessWidget {
  const DriverSection({super.key, required this.title, this.note});
  final String title;
  final String? note;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Semantics(
        header: true,
        child: DriverText(
          title,
          style: driverText(context, 20, 28, weight: FontWeight.w600),
        ),
      ),
      if (note != null) ...[
        const SizedBox(height: 4),
        DriverText(note!, style: driverText(context, 16, 22, secondary: true)),
      ],
    ],
  );
}

/// The centred state panel of the approved empty and offline references (tablet) and its phone
/// card. `prefix` names the keys of its parts.
class DriverStatePanel extends StatelessWidget {
  const DriverStatePanel({
    super.key,
    required this.panelKey,
    required this.prefix,
    required this.icon,
    required this.title,
    required this.copy,
    required this.wide,
    this.detail,
    this.actionLabel,
    this.onAction,
  });
  final String panelKey, prefix, icon, title, copy;
  final String? detail, actionLabel;
  final bool wide;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    if (!wide) {
      return Container(
        key: Key(panelKey),
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: p.bgSurface,
          borderRadius: BorderRadius.circular(44 / 3),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Align(
              alignment: Alignment.centerLeft,
              child: DriverAsset(
                icon,
                size: 48,
                key: Key('$prefix-icon'),
                color: icon.startsWith('phone-') ? null : p.accentPrimary,
              ),
            ),
            const SizedBox(height: 40 / 3),
            Semantics(
              header: true,
              child: DriverText(
                title,
                key: Key('$prefix-title'),
                style: driverText(context, 28, 36, weight: FontWeight.w600),
              ),
            ),
            const SizedBox(height: 40 / 3),
            DriverText(
              copy,
              key: Key('$prefix-copy'),
              style: driverText(context, 20, 28, secondary: true),
            ),
            if (detail != null) ...[
              const SizedBox(height: 40 / 3),
              DriverText(
                detail!,
                key: Key('$prefix-detail'),
                style: driverText(context, 16, 22, secondary: true),
              ),
            ],
          ],
        ),
      );
    }
    return Container(
      key: Key(panelKey),
      constraints: const BoxConstraints(minHeight: 448),
      decoration: BoxDecoration(
        color: p.bgSurface,
        borderRadius: BorderRadius.circular(28),
      ),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Container(
            key: Key('$prefix-icon'),
            width: 80,
            height: 80,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: p.accentSoft,
              borderRadius: BorderRadius.circular(28),
            ),
            child: DriverAsset(icon, size: 32, color: p.accentPrimary),
          ),
          const SizedBox(height: 16),
          SizedBox(
            key: Key('$prefix-title'),
            width: 900,
            child: Semantics(
              header: true,
              child: DriverText(
                title,
                textAlign: TextAlign.center,
                style: driverText(context, 28, 36, weight: FontWeight.w600),
              ),
            ),
          ),
          const SizedBox(height: 16),
          SizedBox(
            key: Key('$prefix-copy'),
            width: 900,
            child: DriverText(
              copy,
              textAlign: TextAlign.center,
              style: driverText(context, 20, 28, secondary: true),
            ),
          ),
          if (actionLabel != null) ...[
            const SizedBox(height: 16),
            SizedBox(
              width: 480,
              child: DriverAction(
                key: Key('$prefix-action'),
                label: actionLabel!,
                onPressed: onAction,
              ),
            ),
          ],
        ],
      ),
    );
  }
}

/// A short instruction card below a state panel, such as how to reach Control.
class DriverContactCard extends StatelessWidget {
  const DriverContactCard({
    super.key,
    required this.cardKey,
    required this.title,
    required this.copy,
    required this.wide,
  });
  final String cardKey, title, copy;
  final bool wide;

  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    return Container(
      key: Key(cardKey),
      padding: EdgeInsets.all(wide ? 24 : 40 / 3),
      decoration: BoxDecoration(
        color: p.bgSurface,
        borderRadius: BorderRadius.circular(wide ? 28 : 44 / 3),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (!wide) ...[
            const Align(
              alignment: Alignment.centerLeft,
              child: DriverAsset('phone-radio', size: 32),
            ),
            const SizedBox(height: 8),
          ],
          DriverText(
            title,
            style: wide
                ? driverText(context, 20, 28, weight: FontWeight.w500)
                : driverText(context, 22, 28, weight: FontWeight.w600),
          ),
          SizedBox(height: wide ? 8 : 8),
          DriverText(copy, style: driverText(context, 20, 28, secondary: true)),
        ],
      ),
    );
  }
}
