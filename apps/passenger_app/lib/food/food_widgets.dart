import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

class FoodHeading extends StatelessWidget {
  const FoodHeading(this.text, {super.key});
  final String text;

  @override
  Widget build(BuildContext context) =>
      Semantics(header: true, child: Text(text, style: SrTheme.of(context).text('title')));
}

class FoodCard extends StatelessWidget {
  const FoodCard({super.key, required this.child, this.tint});
  final Widget child;
  final Color? tint;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: tint ?? theme.palette.bgSurface,
          borderRadius: BorderRadius.circular(theme.metrics.radius['lg']!),
          border: Border.all(color: theme.palette.lineHairline),
        ),
        child: Padding(padding: const EdgeInsets.all(16), child: child),
      ),
    );
  }
}

/// A short notice with an icon; used for empty, error and provider states.
class FoodNotice extends StatelessWidget {
  const FoodNotice({super.key, required this.icon, required this.text, this.action, this.actionLabel});
  final IconData icon;
  final String text;
  final VoidCallback? action;
  final String? actionLabel;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    return FoodCard(
      tint: theme.palette.bgSurface2,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(icon, color: theme.palette.textSecondary),
              const SizedBox(width: 12),
              Expanded(child: Text(text, style: theme.text('body'))),
            ],
          ),
          if (action != null && actionLabel != null) ...[
            const SizedBox(height: 8),
            OutlinedButton(onPressed: action, child: Text(actionLabel!)),
          ],
        ],
      ),
    );
  }
}
