import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'account/account_controller.dart';
import 'account/account_page.dart';
import 'food/food_controller.dart';
import 'food/food_pages.dart';
import 'l10n/app_localizations.dart';
import 'preferences.dart';
import 'trains/train_controller.dart';
import 'trains/train_pages.dart';

/// The four passenger destinations, in this order. Home and Trains stay separate screens.
enum PassengerTab { home, trains, food, account }

/// Header, the four tabs and the floating navigation bar. Opens on Trains. Every tab except
/// Account is public: nothing here waits for sign-in or the account request.
class PassengerShell extends StatefulWidget {
  const PassengerShell({
    super.key,
    required this.account,
    required this.preferences,
    required this.trains,
    required this.food,
  });

  final AccountController account;
  final PreferencesController preferences;
  final TrainController trains;
  final FoodController food;

  @override
  State<PassengerShell> createState() => _PassengerShellState();
}

class _PassengerShellState extends State<PassengerShell>
    with WidgetsBindingObserver {
  PassengerTab _tab = PassengerTab.trains;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    widget.trains.paused = state != AppLifecycleState.resumed;
    if (state == AppLifecycleState.resumed) {
      widget.trains.refresh();
      if (widget.food.journey != null) {
        widget.food.refreshOptions();
        widget.food.refreshMenu();
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final locale = Localizations.localeOf(context).languageCode;
    final gap = theme.metrics.space[16]!;

    String label(PassengerTab tab) => switch (tab) {
      PassengerTab.home => t.tabHome,
      PassengerTab.trains => t.tabTrains,
      PassengerTab.food => t.tabFood,
      PassengerTab.account => t.tabAccount,
    };

    return Scaffold(
      body: SafeArea(
        child: Column(
          children: [
            Padding(
              padding: EdgeInsets.fromLTRB(gap * 1.5, gap, gap * 1.5, gap / 2),
              child: Row(
                children: [
                  Expanded(
                    child: Semantics(
                      header: true,
                      child: Text(t.appTitle, style: theme.text('heading')),
                    ),
                  ),
                  Flexible(
                    child: Text(
                      srMessages['footer.simulation']![locale] ??
                          srMessages['footer.simulation']!['en']!,
                      textAlign: TextAlign.end,
                      style: theme.text(
                        'caption',
                        color: theme.palette.textSecondary,
                      ),
                    ),
                  ),
                ],
              ),
            ),
            Expanded(
              child: IndexedStack(
                index: _tab.index,
                children: [
                  for (final tab in PassengerTab.values)
                    ExcludeFocus(
                      excluding: tab != _tab,
                      child: tab == PassengerTab.account
                          ? AccountPage(
                              controller: widget.account,
                              preferences: widget.preferences,
                            )
                          : tab == PassengerTab.home
                          ? TrainsPage(
                              controller: widget.trains,
                              home: true,
                              onShowTrains: () =>
                                  setState(() => _tab = PassengerTab.trains),
                            )
                          : tab == PassengerTab.trains
                          ? TrainsPage(controller: widget.trains)
                          : FoodPage(
                              controller: widget.food,
                              active: _tab == PassengerTab.food,
                            ),
                    ),
                ],
              ),
            ),
            _NavBar(
              label: t.navLabel,
              tabs: [for (final tab in PassengerTab.values) (tab, label(tab))],
              selected: _tab,
              onSelect: (tab) => setState(() => _tab = tab),
            ),
          ],
        ),
      ),
    );
  }
}

const _icons = {
  PassengerTab.home: (Icons.home_outlined, Icons.home),
  PassengerTab.trains: (Icons.train_outlined, Icons.train),
  PassengerTab.food: (Icons.restaurant_outlined, Icons.restaurant),
  PassengerTab.account: (Icons.person_outline, Icons.person),
};

/// The floating pill navigation from the hi-fi design.
class _NavBar extends StatelessWidget {
  const _NavBar({
    required this.label,
    required this.tabs,
    required this.selected,
    required this.onSelect,
  });

  final String label;
  final List<(PassengerTab, String)> tabs;
  final PassengerTab selected;
  final ValueChanged<PassengerTab> onSelect;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final palette = theme.palette;
    final gap = theme.metrics.space[16]!;

    // Labels grow with the system text size up to 160%; the bar must not outgrow the screen.
    return MediaQuery.withClampedTextScaling(
      maxScaleFactor: 1.6,
      child: Semantics(
        container: true,
        explicitChildNodes: true,
        label: label,
        child: Padding(
          padding: EdgeInsets.fromLTRB(gap, gap / 2, gap, gap),
          child: DecoratedBox(
            decoration: BoxDecoration(
              color: palette.bgSurface,
              borderRadius: BorderRadius.circular(
                theme.metrics.radius['xl']! + 8,
              ),
              border: Border.all(color: palette.lineHairline),
              boxShadow: [
                BoxShadow(
                  color: palette.effectShadowKey,
                  blurRadius: 24,
                  offset: const Offset(0, 8),
                ),
              ],
            ),
            child: Padding(
              padding: EdgeInsets.all(gap / 4),
              child: Row(
                children: [
                  for (final (tab, text) in tabs)
                    Expanded(
                      child: _NavItem(
                        key: Key('tab-${tab.name}'),
                        label: text,
                        icons: _icons[tab]!,
                        selected: tab == selected,
                        onTap: () => onSelect(tab),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  const _NavItem({
    super.key,
    required this.label,
    required this.icons,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final (IconData, IconData) icons;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final palette = theme.palette;
    final ink = selected ? palette.navActiveInk : palette.textSecondary;
    final style = theme.text('label', color: ink).copyWith(letterSpacing: 0);

    return Semantics(
      button: true,
      selected: selected,
      inMutuallyExclusiveGroup: true,
      label: label,
      onTap: onTap,
      child: ExcludeSemantics(
        child: Material(
          color: selected ? palette.navActive : Colors.transparent,
          borderRadius: BorderRadius.circular(theme.metrics.radius['full']!),
          child: InkWell(
            borderRadius: BorderRadius.circular(theme.metrics.radius['full']!),
            onTap: onTap,
            child: ConstrainedBox(
              constraints: BoxConstraints(
                minHeight: theme.metrics.minTarget + 8,
              ),
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 6),
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Icon(selected ? icons.$2 : icons.$1, color: ink, size: 24),
                    const SizedBox(height: 2),
                    // Shrinks a long Sinhala or Tamil label instead of clipping it.
                    FittedBox(
                      fit: BoxFit.scaleDown,
                      child: Text(label, style: style, maxLines: 1),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
