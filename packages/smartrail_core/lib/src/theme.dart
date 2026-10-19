import 'package:flutter/material.dart';

import 'generated/tokens.g.dart';

const _packageName = 'smartrail_core';

/// What widgets need to know about the active profile: colours, sizes and the language.
/// Added to the [ThemeData] by [srTheme]; read it with [SrTheme.of].
@immutable
class SrTheme extends ThemeExtension<SrTheme> {
  const SrTheme({
    required this.profile,
    required this.palette,
    required this.metrics,
    required this.languageCode,
  });

  final SrProfile profile;
  final SrPalette palette;
  final SrMetrics metrics;
  final String languageCode;

  static SrTheme of(BuildContext context) {
    final theme = Theme.of(context).extension<SrTheme>();
    assert(theme != null, 'Build the app theme with srTheme() from smartrail_core.');
    return theme!;
  }

  /// A text style for a named role such as `body` or `heading`, with the line height stretched
  /// for Sinhala and Tamil. [role] must exist in the profile; see tokens.json.
  TextStyle text(String role, {Color? color}) {
    final def = metrics.type[role];
    assert(def != null, 'The $profile profile has no "$role" text role.');
    return _style(def!, languageCode, color ?? palette.textPrimary);
  }

  /// The first of [roles] that this profile defines (profiles name their roles differently).
  TextStyle textOf(List<String> roles, {Color? color}) =>
      text(roles.firstWhere(metrics.type.containsKey), color: color);

  @override
  SrTheme copyWith({String? languageCode}) => SrTheme(
    profile: profile,
    palette: palette,
    metrics: metrics,
    languageCode: languageCode ?? this.languageCode,
  );

  @override
  SrTheme lerp(ThemeExtension<SrTheme>? other, double t) => this;
}

TextStyle _style(SrTextRole role, String languageCode, Color color) {
  // Script-specific roles already carry their measured line box.
  final lineScale = role.fontKey == 'latin' ? (srLineScale[languageCode] ?? 1) : 1.0;
  return TextStyle(
    fontFamily: srFontFamilies[role.fontKey],
    fontFamilyFallback: [
      for (final key in const ['latin', 'sinhala', 'tamil'])
        if (key != role.fontKey) srFontFamilies[key]!,
    ],
    package: _packageName,
    fontSize: role.size,
    fontWeight: FontWeight.values[(role.weight ~/ 100) - 1],
    // Abhaya Libre is the static Regular face; Noto fonts use the weight axis.
    fontVariations: role.fontKey == 'sinhala'
        ? null
        : [FontVariation('wght', role.weight.toDouble())],
    height: role.height * lineScale,
    letterSpacing: role.letterSpacing,
    color: color,
  );
}

/// The Material theme for one app profile and brightness. [locale] only changes line heights,
/// because Sinhala and Tamil stack taller than Latin.
ThemeData srTheme(SrProfile profile, Brightness brightness, {Locale locale = const Locale('en')}) {
  final palette = srPalette(profile, brightness);
  final metrics = srMetrics(profile);
  final extension = SrTheme(
    profile: profile,
    palette: palette,
    metrics: metrics,
    languageCode: locale.languageCode,
  );

  TextStyle role(List<String> names) => extension.textOf(names);

  final textTheme = TextTheme(
    displayLarge: role(const ['display-xl', 'display']),
    displayMedium: role(const ['display-l', 'display', 'clock']),
    displaySmall: role(const ['number', 'time', 'clock']),
    headlineLarge: role(const ['title', 'heading']),
    headlineMedium: role(const ['title', 'heading']),
    headlineSmall: role(const ['heading', 'title']),
    titleLarge: role(const ['heading', 'title']),
    titleMedium: role(const ['body-l', 'strong', 'board', 'body']),
    titleSmall: role(const ['strong', 'body']),
    bodyLarge: role(const ['body-l', 'body']),
    bodyMedium: role(const ['body']),
    bodySmall: role(const ['caption', 'small', 'body']),
    labelLarge: role(const ['button', 'strong', 'label', 'body']),
    labelMedium: role(const ['nav', 'label', 'caption']),
    labelSmall: role(const ['label', 'caption']),
  );

  final scheme = ColorScheme(
    brightness: brightness,
    primary: palette.controlFill,
    onPrimary: palette.controlInk,
    secondary: palette.accentPrimary,
    onSecondary: palette.textOnAccent,
    error: palette.statusDangerInk,
    onError: palette.bgSurface,
    surface: palette.bgSurface,
    onSurface: palette.textPrimary,
    onSurfaceVariant: palette.textSecondary,
    outline: palette.textTertiary,
    outlineVariant: palette.lineHairline,
    surfaceContainerLowest: palette.bgCanvas,
    surfaceContainerLow: palette.bgSurface2,
    surfaceContainer: palette.bgSurface2,
    surfaceContainerHigh: palette.bgSurface2,
    surfaceContainerHighest: palette.bgMap,
    primaryContainer: palette.accentSoft,
    onPrimaryContainer: palette.textPrimary,
  );

  final target = Size(metrics.minTarget, metrics.minTarget);
  final radius = BorderRadius.circular(metrics.radius['full']!);

  // A visible focus ring for keyboards and switch access: 3 dp in the accent colour.
  BorderSide? focusSide(Set<WidgetState> states, BorderSide? rest) =>
      states.contains(WidgetState.focused) ? BorderSide(color: palette.accentPrimary, width: 3) : rest;

  ButtonStyle button({BorderSide? rest}) => ButtonStyle(
    minimumSize: WidgetStatePropertyAll(target),
    shape: WidgetStatePropertyAll(RoundedRectangleBorder(borderRadius: radius)),
    textStyle: WidgetStatePropertyAll(role(const ['button', 'strong', 'body'])),
    side: WidgetStateProperty.resolveWith((states) => focusSide(states, rest)),
  );

  return ThemeData(
    useMaterial3: true,
    brightness: brightness,
    colorScheme: scheme,
    scaffoldBackgroundColor: palette.bgCanvas,
    canvasColor: palette.bgCanvas,
    dividerColor: palette.lineHairline,
    focusColor: palette.accentGlow.withAlpha(0x40),
    textTheme: textTheme,
    fontFamily: srFontFamilies['latin'],
    fontFamilyFallback: [srFontFamilies['sinhala']!, srFontFamilies['tamil']!],
    package: _packageName,
    appBarTheme: AppBarTheme(
      backgroundColor: palette.bgSurface,
      foregroundColor: palette.textPrimary,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      scrolledUnderElevation: 0,
      titleTextStyle: role(const ['heading', 'title']),
    ),
    filledButtonTheme: FilledButtonThemeData(style: button()),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: button(rest: BorderSide(color: palette.textTertiary, width: 1.5)),
    ),
    textButtonTheme: TextButtonThemeData(style: button()),
    iconButtonTheme: IconButtonThemeData(
      style: ButtonStyle(minimumSize: WidgetStatePropertyAll(target)),
    ),
    visualDensity: VisualDensity.standard,
    materialTapTargetSize: MaterialTapTargetSize.padded,
    extensions: [extension],
  );
}
