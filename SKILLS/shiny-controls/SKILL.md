---
name: shiny-controls
description: Generate UI for .NET MAUI (Shiny.Maui.Controls) and Blazor (Shiny.Blazor.Controls) - includes TableView with 14 cell types, TreeView with lazy loading, drag/drop reorder (above/below/into), and configurable expand/collapse icons, FloatingPanel/OverlayHost/ShinyContentPage (bottom/top overlay panels) with detents and header peek, DurationPicker (duration picker with FloatingPanel), FrostedGlassView (native blur/glass effect), Toast service (code-invoked toast notifications with queue/stack, auto-dismiss, spinner, progress bar, pill/fill modes), PillView status badges, BadgeView (content-wrapping corner badge with text/dot/count overflow and pulse), ShinyImage (remote image loading on both hosts — placeholder artwork, a loading ring that fills to a real percentage when the response carries a Content-Length and spins when it does not or when the request is still queued, error artwork, and full LoadingTemplate/ErrorTemplate overrides; on MAUI an IImageService owns memory + disk caching with LRU trimming and expiry, a bounded download queue, and de-duplication of concurrent requests for the same URI, with a replaceable IImageDownloader for authenticated images and ClearCacheAsync/GetCacheSizeAsync/PrefetchAsync for cache management, and MAUI additionally renders SVG as vectors rather than a rasterized bitmap - loaded from an embedded resource via resource://, a file path, a bundled asset, a data: URI or a remote URL, with the format detected from the payload rather than the extension, parsed documents shared through an LRU SvgCache so a list showing one icon parses it once, and SvgTintColor resolving currentColor per placement; on Blazor the image is streamed through fetch for genuine progress with an automatic plain-img fallback when CORS blocks it, and caching is left to the browser), ImageViewer with pinch/pan/double-tap zoom, ImageEditor with crop/rotate/draw/text/undo/redo/export, ChatView with bubbles/typing/load-more/input-bar and custom MessageTemplate/MessageTemplateSelector for per-message rendering, SecurityPin entry, PasswordStrength (both hosts, core packages - a password field built on TextEntry with a live segmented-or-bar strength meter, a rule checklist and a Show/Hide toggle; the defaults are passphrase-first per NIST SP 800-63B - fifteen characters, commonly breached values refused, and every character-composition rule off - and the property to gate a submit button on is IsAcceptable, not Score, because a long passphrase can score 100 and still fail a policy that demands a digit; scoring goes through a pluggable async IPasswordStrengthEvaluator so zxcvbn or an HIBP k-anonymity range query can replace the built-in entropy heuristic, with keystroke debouncing, cancellation of the superseded evaluation, and a fall back to the built-in scorer if a custom one throws), ShinyButton (stateful button on both hosts with Normal/Busy/Success/Error states, per-state text and icons, an auto-revert delay, three busy modes (ReplaceLeftIcon/ReplaceContent/KeepContent), leading and trailing icon slots that each take an ImageSource, a motion-icon name, or an arbitrary view, and Appearance × Type styling (Filled/Tonal/Outlined/Text/Elevated × Primary/Secondary/Success/Warning/Critical/Info) resolved from the theme tokens; on MAUI it follows its Command's CanExecute through IsEnabledCore — so an explicitly disabled button stays disabled — and drives its own busy state while an async command runs, with no IsBusy binding needed; on Blazor the equivalent is that Clicked is awaited), Fab and FabMenu (floating action button and expanding action menu), Scheduler views (calendar grid, agenda timeline, event list), Markdown controls (MarkdownView renderer, MarkdownEditor with toolbar), Barcodes & QR codes (separate Shiny.Maui.Controls.Barcodes / Shiny.Blazor.Controls.Barcodes packages — BarcodeView and QRCodeView with 13 symbologies including QR, Aztec, Data Matrix, PDF417, Code 128/39/93, Codabar, EAN-8/13, UPC-A/E, ITF — pure-managed ZXing.Net renderer, PNG output via built-in encoder on MAUI, SVG or PNG data-URI on Blazor, with a static BarcodeRenderer for raw bytes / SVG / data-URI from code), CameraView (separate Shiny.Maui.Controls.Camera / Shiny.Blazor.Controls.Camera packages — cross-platform camera preview on iOS, Android, Windows, macOS AppKit, and Blazor WASM with zoom, torch, lens/device selection, photo + video capture, and live color filters (Mono/Noir/Sepia/Vivid/Cool/Warm/Fade/Chrome/Instant/Tonal) applied to the live preview and captured photos; a pluggable IFrameAnalyzer pipeline where each analyzer raises its own strongly-typed event (or bindable Command), can be declared right in XAML, added/removed live or toggled on/off via IsEnabled, and draws styled OverlayBoxes via the built-in CameraOverlayView; modular analyzer add-ons Shiny.Maui.Controls.Camera.Barcode/.Face/.Motion/.Ocr/.Documents scan barcodes, detect faces and motion (a box per distinct moving region), run OCR, and extract structured documents — invoices with order lines, AAMVA driver's licenses, health cards, credit cards (brand via IIN+Luhn), and passports (deterministic MRZ) each as their own analyzer returning a strong record with nullable fields + a typed event; registered with .UseShinyCamera()), MediaElement (separate Shiny.Maui.Controls.MediaElement / Shiny.Blazor.Controls.MediaElement packages plus Shiny.Maui.Controls.MediaElement.Linux for GTK4 — local and remote audio/video on iOS, Android, Windows, macOS AppKit, Linux GTK4 and Blazor with a Shiny-drawn transport bar whose play/pause, seek scrubber, volume and fullscreen pieces each toggle independently, Play/Pause/Stop/Seek/Mute commands (methods on Blazor), background audio with OS lock-screen controls, and Picture-in-Picture), AutoCompleteEntry with debounced search and dropdown suggestions, CountryPicker with flag/dial code, AddressEntry with geocoding, SignaturePad for capturing signatures with canvas drawing and PNG export, TextEntry with animated floating placeholder/customizable border/tool slots/validation hints/character count, Slider with two-color gradient track and blended thumb, ProgressBar with gradient fill and Vista-style shimmer pulse sweep, ParallaxCollectionView (MAUI) / ParallaxList (Blazor) — a scrollable list with a hero header that translates at a configurable fraction of the scroll offset, with optional collapse-to-sticky and fade, Overlay/LoadingOverlay (full-screen overlay with configurable color/opacity, custom content template, and built-in loading mode with indeterminate spinner or determinate progress bar), Expander and Accordion (both hosts, core packages - a disclosure panel whose Fade/Slide/Height reveals are flags that combine, aimed at any of the four edges via SlideFrom, opening down or up, with cancelable Expanding/Collapsing, lazy content and a rotating or swapping indicator; Accordion stacks them with Single/Multiple selection, AllowCollapseAll, a two-way ExpandedIndex, and motion/chrome properties that seed every item that did not set them itself), SkeletonView (content-wrapping skeleton loader that shows animated shimmer placeholders while IsBusy is true, with built-in line placeholders or a custom placeholder template), SplashScreen (Blazor-only pre-boot loading splash - static index.html markup plus a classic splash.js global that paints before Blazor starts, driven afterwards by ISplashScreen/SplashScreenHost for status, progress and the handoff; customizable via data attributes, a show() config, or your own markup), Desktop add-on (separate Shiny.Maui.Controls.Desktop package) combining Tray Icon (cross-platform system tray / status-bar icon with context menus, click events, tooltips, and dynamic visibility on Windows/macOS AppKit/MacCatalyst/Linux) and Docking (Visual-Studio-style dockable tool windows, tabbed groups, splitters, auto-hide rails, and tear-off floating windows), On-Screen Keyboard (Blazor only, in the main Shiny.Blazor.Controls package - a touch / kiosk US-QWERTY soft keyboard with a symbols layer, bottom-docked auto-show on focus, momentary Shift and sticky Caps Lock, auto-repeat, caret-aware arrows, and the crucial property that tapping a key never takes the caret off the field; the MAUI half is planned but NOT implemented, so never emit UseOnScreenKeyboard / IOnScreenKeyboard); Blazor's docking and on-screen keyboard both ship in the main Shiny.Blazor.Controls package - there is no Blazor add-on, Keyframe animation (MAUI-only Shiny.Maui.Controls.Keyframe package — declarative CSS-@keyframes-style XAML timelines via the kf:Animate.Keyframes attached property with Keyframes/Track/Key, named + CSS-function easing including cubic-bezier/steps/spring, a fluent C# TimelineBuilder and composable Storyboard with Add/Then/With/Stagger, and a seekable/reversible Player — evaluation is a pure function of time so animations scrub from a Slider or gesture, reverse mid-flight, and export deterministically, which MAUI's own fire-and-forget Animation.Commit cannot do; an AOT-safe hand-registered AnimatableProperties registry, KeyframeView rendering a KeyframeScene layer tree to a canvas with two-way bindable Progress, and an optional Shiny.Maui.Controls.Keyframe.Export package for headless deterministic frame export and GIF encoding), Motion Icons (111 animated icons in the core packages on both hosts - MotionIconView on MAUI and <MotionIcon> on Blazor - triggered by timer/hover/press/appear/command, each with hand-drawn motion plus generic presets (Pulse/Beat/Spin/Shake/Wobble/Bounce/Float/Pop/Tada/Flip/Swing/Blink/Draw/Nudge/Jiggle) that also work on your own path data; one shared MotionSpec compiled to a drawn GraphicsView on MAUI and to CSS @keyframes on Blazor), ModalView (Blazor only, core package - a modal window with an optional title-or-template header, an optional built-in-or-custom close button, a content template and a footer of ModalButtons or your own markup; two-way IsOpen with Opened/cancellable Closing/Closed(reason) events, five sizes, three placements, six animations, a blurrable backdrop, and optional drag/resize/maximize; focus trap, scroll lock, focus restore, aria-modal and modal stacking are built in; the MAUI equivalent is FloatingPanel or the dialog service, never emit ModalView in XAML), StateView and Wizard (both hosts, core packages - StateView switches between named branches from one string with Fade/Slide/Scale transitions and lazy per-branch content; Wizard builds a multi-step flow on it with a drawn pointed-chevron progress bar (or dots/bar/your own), per-step validity gates (IsValid/IsOptional/ValidateCommand/async Validate/cancellable StepChanging), conditional steps that drop out of the run when IsVisible is false, built-in Next/Back/Finish/Cancel commands, and a cancellable Finish), Walkthrough and Tooltip (both hosts, core packages - Walkthrough is a guided tour that dims the page and cuts an animated spotlight around one control at a time, with steps declared together in order on the control rather than attached to each element, four displays (Popover/Tooltip/Inline/Spotlight) or your own template, per-step in/out animations, conditional steps via IsVisible, a RememberRunKey so onboarding runs once, and three ways to advance - the Next command, tapping the highlighted control itself, or a dwell timer; Tooltip is the bubble underneath it, wrapping its target or pointing at one by reference/selector, with auto-flipping placement, a tail that slides to keep pointing after clamping, hover/tap/long-press/focus triggers, and IsOpen (never IsVisible, which is VisualElement's) for binding), Layout (Blazor only - VStack/HStack flexbox stacks, a responsive 12-column Grid/Row/Column whose per-breakpoint spans cascade upwards, and an AppLayout application shell of header/footer/left+right panels placed by CSS grid areas, where each panel cycles hidden -> toolbar rail -> shown, drag-resizes between a min and max, keeps its own scroll region, takes configurable borders, auto-compacts to a scrimmed drawer under a breakpoint measured on the shell, and can persist its state and width), Flyout (MAUI only, core package - a side panel that slides in from either edge, rests as a narrow icon rail instead of a full panel, and either pushes the content aside or floats over it with a scrim; FlyoutPanel/FlyoutView/ShinyFlyoutPage replace FlyoutPage and also work inside Shell, ShinyFlyout.StartTemplate/EndTemplate install one over every page a Shell or NavigationPage shows, and IFlyoutService drives whichever flyout is on the page currently showing; the Blazor equivalent is AppLayoutPanel), TabbedPage (MAUI only, core package - an improved TabbedPage: ShinyTabbedPage hosts tabs whose content is built on first selection and cached, with motion icons, per-tab badges, a direction-aware transition between tabs, and a raised centre button that presents the current page's actions declared as ShinyTabs.Actions the way ToolbarItems are; a ContentTemplate may inflate a whole ContentPage, which is adopted but gets no OnAppearing - implement ITabAware instead; ShinyTabBarBehavior drops the same bar onto a Shell, keeping routes, deep links and per-tab navigation stacks; the Blazor ShinyTabBar component is a different, simpler control), NavigationPage (MAUI only, core package - ShinyNavigationPage is a NavigationPage subclass, so PushAsync/PopAsync/PopToRootAsync/InsertPageBefore/RemovePage, the modal stack, page lifecycle, Android hardware back and Pushed/Popped/PoppedToRoot all keep working; what it adds is toolbar items on the LEFT as well as the right, which no native bar has room for - that slot is the back button's on every platform and AppKit/GTK4 have no bar at all - so the native bar is hidden and a drawn ShinyNavBar replaces it, giving the same left/right items, overflow menu, badges, motion icons and collapsing large title on every head; items are declared on the page as ShinyNav.LeftItems/RightItems holding NavBarItem, which derives from ToolbarItem so an existing toolbar moves over unchanged and a page's own Page.ToolbarItems render on the right automatically; Order=Secondary and anything past MaxVisibleItems fold into an overflow menu; MAUI's own SetHasBackButton/SetBackButtonTitle/SetTitleView/SetTitleIconImageSource/SetIconColor and the Bar* colours are all honoured, with SetHasNavigationBar read only as the starting value because that is the property the class takes over - ShinyNav.IsNavBarVisible is the runtime switch), Captcha (Blazor only, core package - a human check in front of a form: one <Captcha /> over four providers selected by name, the built-in local challenge (canvas characters or a screen-reader-friendly sum, no account and no third-party script) plus Google reCAPTCHA, hCaptcha and Cloudflare Turnstile, registered with AddShinyCaptcha/ConfigureCaptcha so the provider swaps without touching markup; the component yields a token, not a verdict, so pair it with the Validate callback that runs your server's siteverify and gate submit on ValidChanged rather than IsSolved; site keys are public and the secret key never leaves the server, and the local challenge is checked in the browser so it is a bot speed bump rather than a security boundary), Feedback Service (extensible IFeedbackService with haptic default, replaceable with TTS/sound/analytics), and UseFeedback support across all interactive controls
auto_invoke: true
triggers:
  - tableview
  - table view
  - settings page
  - settings view
  - settingsview
  - treeview
  - tree view
  - tree control
  - hierarchical view
  - hierarchy
  - file browser
  - folder browser
  - folder picker
  - directory tree
  - lazy load tree
  - expandable list
  - expander
  - accordion
  - accordion list
  - collapsible
  - collapsible panel
  - collapsible section
  - disclosure
  - disclosure panel
  - expand collapse
  - expander control
  - faq list
  - show more
  - show advanced options
  - nested list
  - tree node
  - org chart
  - blazor treeview
  - blazor tree view
  - sheet view
  - sheetview
  - bottom sheet
  - bottomsheet
  - floating panel
  - floatingpanel
  - flyout
  - flyoutpage
  - flyout page
  - shinyflyoutpage
  - flyoutview
  - flyoutpanel
  - drawer
  - navigation drawer
  - nav drawer
  - side panel
  - sidebar
  - side menu
  - hamburger menu
  - slide out
  - slideout
  - collapsible sidebar
  - nav rail
  - navigation rail
  - icon rail
  - inspector panel
  - push content
  - shell flyout
  - replace shell flyout
  - navigationpage
  - navigation page
  - shinynavigationpage
  - shinynavbar
  - nav bar
  - navbar
  - navigation bar
  - toolbaritems
  - toolbar items
  - left toolbar item
  - right toolbar item
  - left and right toolbar items
  - back button
  - back button behavior
  - large title
  - collapsing title
  - overflow menu
  - tabbedpage
  - tabbed page
  - shinytabbedpage
  - shinytabbar
  - tab bar
  - tabbar
  - bottom tabs
  - bottom tab bar
  - bottom navigation
  - tab navigation
  - tabs
  - tab badge
  - center button
  - centre button
  - raised tab button
  - shell tab bar
  - replace shell tab bar
  - custom tab bar
  - animated tabs
  - tab transition
  - overlay host
  - overlayhost
  - shiny content page
  - shinycontentpage
  - pill
  - badge
  - status badge
  - scheduler
  - calendar
  - agenda
  - event list
  - calendar view
  - timeline
  - shinyimage
  - shiny image
  - remote image
  - image loading
  - image loader
  - load image from url
  - image from url
  - image placeholder
  - image error image
  - image download progress
  - cached image
  - image cache
  - imageservice
  - image service
  - ffimageloading
  - lazy image
  - authenticated image
  - image viewer
  - imageviewer
  - image zoom
  - pinch to zoom
  - photo viewer
  - image editor
  - imageeditor
  - image editing
  - crop image
  - draw on image
  - annotate image
  - image annotation
  - photo editor
  - media picker
  - media element
  - mediaelement
  - media player
  - video player
  - audio player
  - play video
  - play audio
  - video playback
  - audio playback
  - background audio
  - background playback
  - picture in picture
  - picture-in-picture
  - transport controls
  - seek bar
  - scrubber
  - exoplayer
  - avplayer
  - video control
  - mp4
  - hls
  - mediapicker
  - media picker button
  - photo picker
  - pick photo
  - pick image
  - choose photo
  - take photo
  - add photos
  - image upload
  - photo upload
  - gallery picker
  - camera capture button
  - compress photo
  - image compression
  - photo carousel
  - markdown
  - markdown view
  - markdown editor
  - markdown preview
  - rich text
  - security pin
  - securitypin
  - pin code
  - pin entry
  - otp
  - one time password
  - password strength
  - passwordstrength
  - strength meter
  - password meter
  - password field
  - password input
  - password validation
  - password policy
  - password rules
  - passphrase
  - zxcvbn
  - have i been pwned
  - hibp
  - compromised password
  - breached password
  - sign up form
  - signup form
  - registration form
  - change password
  - reset password
  - pin control
  - button
  - shinybutton
  - shiny button
  - loading button
  - busy button
  - submit button
  - async button
  - icon button
  - stateful button
  - button with spinner
  - button loading state
  - button with icon
  - fab
  - floating action button
  - floating button
  - fab menu
  - fabmenu
  - speed dial
  - action menu
  - action button
  - shiny blazor controls
  - blazor tableview
  - blazor sheetview
  - blazor bottomsheet
  - blazor floating panel
  - blazor fab
  - blazor pillview
  - blazor imageviewer
  - blazor imageeditor
  - blazor securitypin
  - blazor scheduler
  - blazor markdown
  - blazor mermaid
  - autocomplete
  - auto complete
  - autocompleteentry
  - auto complete entry
  - search input
  - typeahead
  - type ahead
  - country picker
  - countrypicker
  - country selector
  - country search
  - address entry
  - addressentry
  - address search
  - address lookup
  - geocoding
  - geocode
  - blazor autocomplete
  - blazor country picker
  - blazor address entry
  - chat
  - chatview
  - chat view
  - chat bubbles
  - messaging
  - chat control
  - typing indicator
  - blazor chatview
  - signature pad
  - signaturepad
  - signature
  - signature capture
  - sign here
  - e-signature
  - esignature
  - draw signature
  - blazor signaturepad
  - blazor signature
  - text entry
  - textentry
  - text field
  - text input
  - material entry
  - floating placeholder
  - floating label
  - blazor textentry
  - blazor text entry
  - stepper
  - stepper tool
  - textentrystepper
  - gradient slider
  - gradientslider
  - slider
  - range slider
  - rangeslider
  - two thumb slider
  - dual slider
  - min max slider
  - temperature slider
  - blazor slider
  - blazor gradient slider
  - duration picker
  - durationpicker
  - shinydurationpicker
  - frosted glass
  - frostedglass
  - glass effect
  - blur effect
  - acrylic
  - backdrop blur
  - glassmorphism
  - chat template
  - message template
  - chat button
  - action message
  - toast
  - toast notification
  - toast service
  - snackbar
  - show toast
  - blazor toast
  - dialog
  - dialogs
  - alert
  - confirm
  - prompt
  - datagrid
  - data grid
  - grid
  - table with sorting
  - sortable table
  - filterable grid
  - mudblazor datagrid
  - column grid
  - editable grid
  - alert confirm prompt
  - message box
  - blazor dialog
  - feedback service
  - ifeedbackservice
  - haptic
  - haptic feedback
  - custom feedback
  - usefeedback
  - progress bar
  - progressbar
  - progress indicator
  - loading bar
  - gradient progress
  - shimmer
  - vista progress
  - progress line
  - progressline
  - top loading bar
  - loading line
  - page load indicator
  - nprogress
  - youtube loading bar
  - thin progress bar
  - animated progress fill
  - blazor progressbar
  - gradient slider
  - gradientslider
  - range slider
  - temperature slider
  - blazor gradient slider
  - overlay
  - loading overlay
  - loadingoverlay
  - busy overlay
  - spinner overlay
  - progress overlay
  - blazor overlay
  - carousel
  - carousel gallery
  - carouselgallery
  - netflix carousel
  - horizontal scroll
  - snap carousel
  - vstack
  - hstack
  - stack layout
  - blazor stack
  - responsive grid
  - responsive layout
  - 12 column grid
  - grid row column
  - breakpoint
  - breakpoints
  - app layout
  - applayout
  - app shell
  - master layout
  - page layout
  - side panel
  - side panels
  - left panel
  - right panel
  - collapsible panel
  - collapsible sidebar
  - resizable panel
  - resizable sidebar
  - splitter
  - split panel
  - header footer layout
  - dashboard layout
  - admin layout
  - staggered grid
  - staggeredgrid
  - masonry
  - waterfall layout
  - pinterest grid
  - pinterest layout
  - virtualized grid
  - virtualizedgrid
  - grouped grid
  - sticky headers
  - load more
  - parallax
  - parallax collection view
  - parallaxcollectionview
  - parallax list
  - parallaxlist
  - parallax header
  - parallax scroll
  - hero header
  - collapsing header
  - sticky header collapse
  - blazor carousel
  - blazor staggered grid
  - blazor virtualized grid
  - blazor parallax
  - skeleton
  - skeleton loader
  - skeleton view
  - skeletonview
  - shimmer
  - shimmer loading
  - shimmer placeholder
  - loading placeholder
  - content placeholder
  - placeholder loading
  - splash screen
  - splashscreen
  - loading splash
  - boot splash
  - startup screen
  - launch screen
  - preloader
  - pre-boot loader
  - blazor loading screen
  - blank flash on startup
  - white flash on startup
  - index.html loading
  - blazor-load-percentage
  - badgeview
  - badge view
  - notification badge
  - notification dot
  - count badge
  - unread badge
  - unread count
  - inbox badge
  - cart badge
  - corner badge
  - blazor badge
  - blazor badgeview
  - tray icon
  - trayicon
  - system tray
  - system tray icon
  - status bar icon
  - status bar app
  - menu bar icon
  - menu bar app
  - menubar
  - notification area
  - taskbar icon
  - shell_notifyicon
  - notifyicon
  - nsstatusitem
  - nsstatusbar
  - app indicator
  - appindicator
  - libappindicator
  - libayatana-appindicator
  - statusnotifieritem
  - desktop tray
  - maui desktop tray
  - background app
  - tray menu
  - tray context menu
  - file drop
  - filedrop
  - drag and drop files
  - drag drop file
  - drop files on window
  - drop files onto app
  - drag files from finder
  - drag files from explorer
  - file drag drop
  - dropped file
  - drop target
  - drop zone
  - dropgesturerecognizer
  - drop over webview
  - drop on blazorwebview
  - accept dropped files
  - import dropped file
  - ondrop
  - dragover
  - datatransfer files
  - docking
  - dock host
  - dockhost
  - dockable
  - tool window
  - tool windows
  - tabbed panel
  - tabbed group
  - dock panel
  - dockpanel
  - visual studio docking
  - visual studio layout
  - vs docking
  - tear off
  - tear-off
  - floating window
  - floating panel docking
  - splitter
  - dock splitter
  - auto-hide rail
  - auto-hide panel
  - blazor docking
  - quick entry
  - promptview
  - prompt view
  - prompt popup
  - in-app overlay popup
  - quickentry
  - quick entry popup
  - global hotkey
  - global hotkeys
  - hotkey
  - system-wide hotkey
  - spotlight popup
  - command palette popup
  - launcher popup
  - ai prompt bar
  - ai prompt popup
  - claude desktop popup
  - copilot key
  - always on top popup
  - borderless popup
  - floating prompt window
  - screen glow
  - screen edge glow
  - siri glow
  - siri border
  - rainbow border
  - listening indicator
  - registerhotkey
  - xgrabkey
  - globalshortcuts portal
  - on-screen keyboard
  - onscreen keyboard
  - on screen keyboard
  - osk
  - virtual keyboard
  - soft keyboard
  - software keyboard
  - touch keyboard
  - kiosk keyboard
  - tablet keyboard
  - touchscreen keyboard
  - touch-screen keyboard
  - blazor keyboard
  - maui keyboard
  - bottom docked keyboard
  - qwerty
  - qwerty keyboard
  - kiosk mode
  - barcode
  - barcodes
  - barcode view
  - barcodeview
  - qr code
  - qrcode
  - qr code view
  - qrcodeview
  - qr generator
  - barcode generator
  - barcode renderer
  - ean
  - ean-13
  - ean13
  - upc
  - upc-a
  - upca
  - code128
  - code 128
  - code39
  - code 39
  - code93
  - pdf417
  - aztec
  - data matrix
  - datamatrix
  - codabar
  - itf
  - itf-14
  - zxing
  - render barcode
  - generate qr
  - generate qr code
  - blazor barcode
  - blazor qr code
  - blazor qrcode
  - camera
  - cameraview
  - camera view
  - camera preview
  - take photo
  - capture photo
  - record video
  - video recording
  - camera filter
  - scan barcode
  - barcode scanner
  - qr scanner
  - scan qr code
  - face detection
  - detect faces
  - motion detection
  - ocr
  - text recognition
  - read text from camera
  - invoice scanner
  - document scanner
  - scan drivers license
  - drivers license scanner
  - aamva
  - pdf417 scan
  - health card scanner
  - extract invoice line items
  - credit card scanner
  - scan credit card
  - card number ocr
  - passport scanner
  - scan passport
  - mrz reader
  - machine readable zone
  - frame analysis
  - getusermedia
  - webcam
  - torch
  - flashlight
  - flip camera
  - front camera
  - back camera
  - keyframe
  - keyframes
  - animation
  - animate
  - css animation
  - declarative animation
  - xaml animation
  - storyboard
  - easing
  - cubic-bezier
  - spring animation
  - scrub animation
  - seek animation
  - reverse animation
  - ping pong animation
  - animated scene
  - lottie
  - gif export
  - export animation
  - state view
  - stateview
  - view switcher
  - view state
  - state machine ui
  - wizard
  - multi step
  - multi-step form
  - step by step
  - stepper
  - onboarding flow
  - checkout flow
  - step indicator
  - walkthrough
  - guided tour
  - product tour
  - app tour
  - coach mark
  - coachmarks
  - spotlight
  - user onboarding
  - onboarding tour
  - feature tour
  - feature announcement
  - showcase view
  - highlight element
  - tutorial overlay
  - intro.js
  - shepherd
  - tooltip
  - tooltips
  - hint
  - hint bubble
  - popover
  - modal
  - modal dialog
  - modal window
  - modal popup
  - popup window
  - dialog window
  - modalview
  - blazor modal
  - callout
  - info bubble
  - help text
  - tooltip placement
  - progress steps
  - breadcrumb steps
  - captcha
  - recaptcha
  - hcaptcha
  - turnstile
  - human check
  - bot protection
  - anti-bot
  - are you a robot
  - i am not a robot
  - form spam
  - spam protection
  - site key
references:
  - layout.md
  - toolbar-tabbar.md
  - tableview.md
  - datagrid.md
  - spreadsheet.md
  - document-viewer.md
  - document-editor.md
  - slide-editor.md
  - notebook.md
  - treeview.md
  - floating-panel.md
  - flyout.md
  - tabbedpage.md
  - navigationpage.md
  - pillview.md
  - shiny-image.md
  - image-viewer.md
  - image-editor.md
  - media-picker-button.md
  - chatview.md
  - security-pin.md
  - password-strength.md
  - button.md
  - fab.md
  - markdown.md
  - scheduler.md
  - autocomplete.md
  - country-picker.md
  - address-entry.md
  - signature-pad.md
  - pickers.md
  - frosted-glass.md
  - toast.md
  - dialogs.md
  - modal.md
  - textentry.md
  - slider.md
  - range-slider.md
  - progressbar.md
  - progressline.md
  - overlay.md
  - skeleton.md
  - expander.md
  - wizard.md
  - timeline.md
  - walkthrough.md
  - tooltip.md
  - splash-screen.md
  - badge.md
  - tray-icon.md
  - docking.md
  - ribbon.md
  - quick-entry.md
  - file-drop.md
  - onscreen-keyboard.md
  - barcodes.md
  - camera.md
  - mediaelement.md
  - keyframe.md
  - motion-icons.md
  - feedback-service.md
  - captcha.md
---

# Shiny Controls Skill

You are an expert in the Shiny Controls library, which ships a single shared control surface across two hosts:
- **.NET MAUI** — `Shiny.Maui.Controls` (plus `Shiny.Maui.Controls.Markdown`, `Shiny.Maui.Controls.MermaidDiagrams`)
- **Blazor** — `Shiny.Blazor.Controls` (plus `Shiny.Blazor.Controls.Markdown`, `Shiny.Blazor.Controls.MermaidDiagrams`)

Every control below is available on **both** MAUI and Blazor. The feature set (properties, events, behavior) is intentionally mirrored — the same concepts apply on either host; only the syntax differs (XAML + `BindableProperty` on MAUI, Razor markup + `[Parameter]` on Blazor).

The library contains:
- **TableView**: A pure MAUI settings-style TableView with 14 cell types, cascading styles, sections, drag-sort reordering, and full MVVM/binding support
- **DataGrid** (MAUI + Blazor): A feature-rich data grid modeled on MudBlazor. Blazor is a generic `DataGrid<TItem>` rendering an HTML `<table>` with child `PropertyColumn`/`TemplateColumn`; MAUI is a pure cross-platform composite (`shiny:DataGrid` + `DataGridColumn`/`DataGridTemplateColumn`, items as `object`) built on a `Grid` header + virtualized `CollectionView` (no native handlers). Sorting (single/multi), filtering (menu/row/toolbar), grouping + aggregates, single/multi selection w/ checkboxes, inline editing (cell/form), paging, virtualization, column resize/reorder, sticky header, `ServerData` delegate, density/striped/bordered/hover. See datagrid.md
- **Spreadsheet** (MAUI + Blazor WASM): `SpreadsheetView` opens, renders and edits real `.xlsx` workbooks. A shared host-agnostic kernel (`Shiny.Controls.Office.Shared`) owns the OOXML package, a transactional undo stack and a formula engine (~80 functions, dependency-ordered recalculation, circular-reference detection); a shared SkiaSharp painter (`Shiny.Controls.Office.Skia`) draws the grid for both hosts, so MAUI and Blazor are literally the same renderer. Virtualized grid over 1,048,576 rows, frozen panes, merged cells, column/row resize, range selection, in-cell editing via a native `Entry`/`<input>`. Edits are surgical: an unmodified workbook saves byte-identical, and macros, tracked changes, pivot caches and custom XML survive untouched. MAUI requires `UseShinyOffice()` (SkiaSharp plus, on `net10.0-macos`, the AppKit canvas SkiaSharp does not ship); Blazor is WASM-only. Row/column insert-delete is deliberately not implemented. **Find** is on the Home tab of all three Office toolbars — a box, a `3/12` readout and previous/next arrows over a shared `IFindController`; the spreadsheet searches cell text as the formula bar shows it (formula first, then literal), the active sheet only unless `SearchAllSheets` is set. See spreadsheet.md
- **Document & Slide Viewers** (MAUI + Blazor WASM): `DocumentView` renders `.docx`, `SlideView` renders `.pptx`, both **read-only** and both in the same packages as the spreadsheet. Word **reflows** to the control's width rather than paginating — no pages, headers or footers by design — with the full style chain resolved (doc defaults → named style with its whole `basedOn` ancestry → direct formatting), lists numbered from `numbering.xml`, tables with spans/merges/shading, inline images, plus an outline for navigation. PowerPoint **scales** its fixed-size artboards rather than reflowing, with shapes resolved through slide → layout → master (a title placeholder usually carries text and nothing else), ~20 preset geometries, gradient fills, theme colours with lumMod/lumOff/shade/tint applied, per-level text styles, notes, a thumbnail-grid mode, and a **presenting mode** that takes the deck full screen on black with an auto-hiding control bar and speaker notes. Both preserve the package untouched and report what they could not draw via `UnsupportedFeatureCollector`. See document-viewer.md
- **Document Editor** (MAUI + Blazor WASM): two controls — `DocumentEditor` is the lone editing surface, `DocumentEditorView` wraps it in formatting chrome. Edits are surgical on the OOXML runs (runs are split only at the boundaries an edit needs and never rebuilt, so language, proofing state and revision marks survive), with a transactional undo stack, caret and selection over the shared text layout, and typing/Enter/Backspace/formatting/alignment. Both toolbars are built from the same core pickers — FontPickerButton, FontSizePickerButton and ColorPickerButton exist on both hosts; only the bar around them differs (MAUI lays out a row of primitives because it has no toolbar control, Blazor composes ShinyToolbar with the item row as its own flex container, which is what keeps the items off the text baseline and aligned). Blazor keyboard input is complete (via `beforeinput`, so IME and dictation work); **MAUI has no portable key-down event**, so physical keys route through `HandleKey`. **Spell check** uses the platform's own dictionary on MAUI — UITextChecker, NSSpellChecker, Android text services, Windows `ISpellChecker` — registered automatically, with a red wavy underline and a right-click/long-press menu offering corrections, Ignore and Add to dictionary (which writes to the user's real dictionary); the browser exposes no such API so Blazor defaults to none and takes an app-supplied `ISpellChecker`, overridable per control or globally via `SpellCheckers.Default` on either host. **Find** is on the Home tab — a box, a `3/12` readout and previous/next arrows (`OfficeFindBar` over `IFindController`, shared with the slide and spreadsheet editors). Setting `Find.Query` searches and steps onto the first hit at or after the caret and selects it; next/previous wrap; paragraphs only, because a caret position is a block and an offset and a table cell has neither. See document-editor.md
- **Slide Editor** (MAUI + Blazor WASM): two controls — `SlideEditor` is the lone editing surface, `SlideEditorView` wraps it in an editing toolbar. Two gestures carry the whole design: a single click selects a shape and draws a dashed frame with eight resize handles (drag body to move, handle to resize); a double-click puts a caret inside that shape's text and the frame turns solid. Typing is deliberately dropped while a shape is merely selected — `Controller.IsEditingText` gates it. Only shapes the slide itself owns are selectable; layout/master shapes and group children are skipped, because dragging one would move it across every slide using that layout. Edits are surgical on the DrawingML runs, and `a:rPr` children are a *sequence* — out-of-order properties make PowerPoint call the file corrupt rather than repair it. The text layout inside a shape is shared with the painter so caret and glyphs cannot drift. `SetFontSize`/`CaretFormat.FontSize` are in points, not the model's pixels. **Find** is on the Home tab, over the same shared `IFindController`: a deck search spans every slide, opens the one it lands on, selects the shape and selects the matched word, and searches only `IsEditable` shapes — a hit in a layout or master shape would count the company name once per slide. **Home ▸ Slide ▸ Slide show** plays the deck full screen from the slide being edited: the editor grows no presenting mode of its own, it hands a `SlideView` the deck it is already holding, so the show is the viewer's whole (black surround, auto-hiding presenter bar, tap to advance, speaker notes) and plays what was typed a moment ago without a save or a reload. No `F5` here, unlike the viewer — Blazor fixes `preventDefault` a keystroke behind the handler that decides it, and a reload would lose an unsaved deck. Not implemented: soft line breaks, table-cell and grouped-shape editing, adding/reordering slides, rotation handles, replace. See slide-editor.md
- **Notebook** (MAUI + Blazor WASM): two controls — `NotebookEditor` is the lone canvas, `NotebookEditorView` wraps it in a ribbon, section tabs and a page list. A OneNote-style free-form page: unlike a slide, which is a fixed artboard that gets *fitted*, a page has no edges — it grows to hold whatever is on it (`Extent()` = minimum unioned with every item's bounds plus padding) and the canvas scrolls and zooms instead. Three layers of state and keeping them apart is the design: the **tool** decides what a press starts (Select, Text, Shape, Pen, Highlighter, Eraser, Lasso, Pan), the **selection** is a set of item *ids* (a lasso catches thirty strokes and a picture at once — there is no `SelectedShape` int), and **text editing** is a caret inside exactly one item. Escape steps back out one layer at a time. Items are one immutable `NoteItem` record with a `Kind` discriminator, z-ordered by their index in the page's list rather than by a field. Ink is real: pressure normalised 0..1 where 0.5 means "no idea" (only a pen reports otherwise), a highlighter painted *beneath* every other item so it does not grey the words it marks, a point eraser that *splits* a stroke into separate items rather than leaving a hole, and hit-testing and lasso capture against a stroke's path rather than its bounding box. Text formatting reaches a selected container as well as a caret inside one, so a toolbar's `HasText` is `IsEditingText || HasSelection`. Saves to a `.shinynote` zip — `notebook.json`, one JSON per page, pictures as files — and it is the one editor here whose model *is* the truth rather than a projection of an OOXML package, so there is no reproject step and no byte-identical promise. The one Office surface whose page follows the app theme, and existing ink is never recoloured. Not implemented: rotation handles, tables on a page, ink-to-shape recognition, search across pages, tags. See notebook.md
- **Timeline** (MAUI + Blazor): `TimelineView` — a vertical rail of markers with arbitrary content beside each one; the wizard's three-state marker turned on its side and made item-driven. **Named `TimelineView`, not `Timeline`**, because `Shiny.Controls.Keyframe.Timeline` already owns the bare name in a namespace MAUI's core package imports. `ActiveIndex` says how far along it is — before it complete, at it current (drawing a ring), after it pending — and it **defaults to -1**, so a timeline handed no position does not claim its first entry has happened; `AllActive` fills everything and wins outright over the index rather than being merged with it. The rule is `TimelineNode.StateFor(index, activeIndex, allActive)` on both hosts. Rows size to their content and the rail stretches to match, which is why it is built per row rather than as one line behind the stack: content beside a timeline is arbitrary and self-sizing, and a continuous line would need a total height nothing knows until after layout. `ItemTemplate` and `OppositeTemplate` bind to the **item**; `MarkerTemplate` binds to a **TimelineNode**, because everything deciding how a marker is drawn is a property of the node's position rather than of the item. Not virtualized on either host — the deliberate trade for rows of differing height. On MAUI the marker is a `BoxView` rather than a `Border` so its `Color` can take a theme token; a colour token on a `Brush` property is silently dropped. See timeline.md
- **TreeView**: Hierarchical tree with lazy-loaded branches (`ChildrenLoader` for per-node async, `RootLoader` for async root), `ChildrenSelector` for sync data, `HasChildrenSelector`/`CanExpandSelector`/`CanSelectSelector` predicates, configurable `ExpandedIcon`/`CollapsedIcon`/`RetryIcon` (ImageSource on MAUI, RenderFragment slots on Blazor), single/multi selection with two-way `SelectedItem`/`SelectedItems` (multi-select renders a checkbox per row — `ShowSelectionCheckBoxes` to turn it off — and switching modes clears the selection), events + ICommand mirrors for `ItemSelected`/`ItemExpanded`/`ItemCollapsed`/`LoadFailed`/`ItemDropped`, indent + guide lines, drag/drop reorder with above/below/into drop positions and visual drop indicators (event-only — never mutates your data; native HTML5 drag via JS interop on Blazor for Safari/Firefox support, pan-gesture fallback on Catalyst/AppKit/GTK4), programmatic API (`ExpandAll`/`ExpandAllAsync` — both materialize every sync branch and cap at a `maxDepth` of 32 — plus `CollapseAll`/`Expand`/`Collapse`/`SelectAll`/`DeselectAll`/`SetBranchSelected`/`Refresh`/`ReloadAsync` with state preservation/`FindNode`), and keyboard navigation on Blazor
- **FloatingPanel + OverlayHost**: A floating panel overlay system (MAUI only). Panels slide from bottom or top with configurable detents, header peek when closed, backdrop dimming, and feedback. Multiple panels coexist without blocking touches. Use with `OverlayHost` (manual Grid setup) or `ShinyContentPage` (convenience ContentPage with built-in overlay). Blazor uses `SheetView` with CSS-based overlays instead
- **Expander & Accordion** (MAUI + Blazor): A disclosure panel — header plus content that animates in beneath or above it — and the accordion list that coordinates a stack of them. `Animation` is a flags enum (`Fade`/`Slide`/`Height`) so the effects combine; `SlideFrom` aims the slide at any of the four edges and `ExpandDirection` opens the panel down or up. Two-way `IsExpanded`, cancelable `Expanding`/`Collapsing`, `LoadContentOnDemand`, a rotating or swapping indicator on either edge, and full border/radius/fill/padding/separator control that falls back to the theme. `Accordion` adds `SelectionMode` Single/Multiple, `AllowCollapseAll`, a two-way `ExpandedIndex`, `ExpandAll`/`CollapseAll`, `ItemsSource` with header/content templates (MAUI) or a plain `@foreach` (Blazor), and motion/chrome defaults that reach every item that did not set them itself. On MAUI `Height` measures and animates a clipped panel; on Blazor it is a CSS `grid-template-rows` transition with no JS. See expander.md
- **PillView**: A status badge/label control with 6 preset themes, custom colors, and WCAG-accessible contrast
- **BadgeView**: A content-wrapping overlay that pins a small badge to one of the four corners (`TopLeft`/`TopRight`/`BottomLeft`/`BottomRight`) of a wrapped view. Setting `Text` to an empty string auto-hides the badge — bind your unread/count value directly. Supports configurable `BadgeColor`/`BadgeTextColor`/`BadgeBorderColor`/`BadgeBorderThickness`, `IsDot` mode for simple notification indicators, `MaxCount` numeric overflow rendering ("99+"), per-corner `OffsetX`/`OffsetY` nudge (default hangs the badge slightly outside the corner), scale-in/out animation (`IsAnimated`), and optional continuous `IsPulsing` to draw attention. Blazor honors `prefers-reduced-motion`
- **ImageViewer**: A tappable thumbnail plus a full-screen image overlay with pinch-to-zoom, pan when zoomed, double-tap to toggle zoom, animated open/close, and a close button. On MAUI both surfaces are a `ShinyImage`, so binding `Uri` instead of `Source` adds placeholder artwork, a loading ring, error artwork and `IImageService` memory/disk caching — the overlay opens off the cache the thumbnail already warmed. See image-viewer.md
- **ImageEditor**: An inline image editor with cropping (drag-handle selection with dimmed overlay), rotation, freehand drawing with color, text annotations, undo/redo, reset, and export to PNG/JPEG/WEBP at configurable resolutions
- **MediaPickerButton**: A button that adds photos from the gallery and/or camera (built-in `MediaPicker` on MAUI; `<input type=file>`/`capture` on Blazor), compresses/re-encodes each to PNG or JPEG at a chosen quality (with optional max-dimension downscale), caps the count with `MaxPhotos` (added one at a time), and shows the collected photos inline as a tappable carousel (`ShowAsCarouselInView`, opening the ImageViewer with an optional Edit button that reuses the ImageEditor) or a compact pinch/zoom overlay. `AllowGallery`/`AllowCamera`/`AllowPhotoEdit` toggles, `PermissionDeniedText`, `NoImagesTemplate`, and a two-way `Photos` collection of `MediaPickerItem`. See media-picker-button.md
- **ChatView**: A modern chat UI with message bubbles, per-participant colors and avatars, visual grouping by sender/minute, typing indicators, virtualized message list with load-more, auto-link detection, image messages, and a bottom input bar with send/attach
- **SecurityPin**: A PIN/OTP entry control with individual cells, configurable length, keyboard, and optional character masking
- **PasswordStrength** (MAUI + Blazor): A password field with a live strength meter and a rule checklist, built on TextEntry. Passphrase-first defaults (15 characters, breached values refused, composition rules off). Gate submit on `IsAcceptable`, not `Score`. Pluggable async `IPasswordStrengthEvaluator` for zxcvbn / HIBP. See password-strength.md
- **Fab**: A Material-style floating action button with Icon, Text, Command, custom colors, border, and shadow
- **FabMenu**: A floating action menu with an expanding, animated child `FabMenuItem` stack and two-way `IsOpen`
- **SchedulerCalendarView**: Monthly calendar grid with swipe navigation, event display, and pinch-to-zoom
- **SchedulerAgendaView**: Day/multi-day timeline (`DaysToShow` 1–7) with overlap detection, switchable date picker modes (`DatePickerMode`: Carousel / Calendar / None), additional timezone columns, auto-updating current time marker, and 12/24-hour time — full feature parity on MAUI and Blazor
- **SchedulerCalendarListView**: Vertically scrolling event list grouped by day with infinite scroll and sticky day headers (`StickyDayHeaders`, default true, pins the current day header while scrolling)
- **MarkdownView**: A read-only markdown renderer that converts markdown text to native MAUI controls with theming and link handling
- **MarkdownEditor**: A markdown editor with formatting toolbar, live preview toggle, and customizable toolbar items
- **BarcodeView / QRCodeView** (separate `Shiny.Maui.Controls.Barcodes` / `Shiny.Blazor.Controls.Barcodes` packages): Pure-managed barcode rendering powered by ZXing.Net. Supports 13 symbologies (`QRCode`, `Aztec`, `DataMatrix`, `Pdf417`, `Code128`, `Code39`, `Code93`, `Codabar`, `Ean8`, `Ean13`, `UpcA`, `UpcE`, `Itf`). MAUI renders to PNG via a built-in pure-managed encoder (no SkiaSharp / `System.Drawing` dependency, AOT-safe) and feeds an `Image`. Blazor renders inline SVG by default (crisp at any size, single-path output with `shape-rendering="crispEdges"`) or a PNG `data:` URI. `QRCodeView` is a `BarcodeView` subclass that locks `Format = QRCode` and adds `Size` (square edge length) and `ErrorCorrection` (`Low`/`Medium`/`Quartile`/`High`). The static `BarcodeRenderer` exposes `RenderPng`, `RenderSvg`, and `RenderDataUri` for raw output without a view. XAML namespace `xmlns:bc="http://shiny.net/maui/barcodes"`
- **AutoCompleteEntry**: A text input with debounced search, dropdown suggestions, busy indicator, custom item templates, and full styling control via CSS custom properties (Blazor) or bindable properties (MAUI)
- **CountryPicker**: A country search control built on AutoCompleteEntry with flag emoji, country name, and dial code
- **AddressEntry**: An address search control built on AutoCompleteEntry with geocoding (Nominatim/OpenStreetMap by default) and structured address results
- **SignaturePad**: A signature capture control that opens in a FloatingPanel (MAUI) or SheetView (Blazor). Users draw on a canvas and export to PNG. Configurable stroke color/width, background, export dimensions, sign/cancel buttons, and panel styling. Like FloatingPanel, it must be placed inside an `OverlayHost` or `ShinyContentPage` (MAUI). The Sign button is disabled until the user draws something
- **Toast**: A service-first toast notification system invoked via DI-injected `IToaster` (registered by `UseShinyControls()`). Supports auto-dismiss with configurable duration, manual dismiss via `IDisposable`, pill or fill-horizontal display modes, top/bottom positioning, queue or stack mode for multiple toasts, indeterminate spinner, countdown progress bar, icon, tap command, feedback, and screen reader announcement. No XAML or OverlayHost required — the overlay auto-attaches to the current page. Blazor uses `IToastService` with `<ToastHost>` component
- **Dialogs** (MAUI + Blazor): A service-first dialog system that emulates `alert`/`confirm`/`prompt` with owned (non-native), animated, themeable dialogs. Inject `IDialogService` and await `Alert` (Task), `Confirm` (Task<bool>), or `Prompt` (Task<PromptResult>). Queued/modal, backdrop cancel (Escape/Enter on Blazor), theme-token colors. Per-call `configure` sets the `DialogAnimation` (None/Fade/SlideTop/SlideBottom/SlideLeft/SlideRight/Zoom/Pop) and styling; customize globally via `ConfigureDialogs` (MAUI) / `AddShinyDialogs(o => ...)` (Blazor) or fully replace the card with a `ContentTemplate` (MAUI `DataTemplate`) / `<DialogHost Template>` (Blazor `RenderFragment<DialogContext>`). MAUI auto-attaches (just `UseShinyControls()`); Blazor needs `AddShinyDialogs()` + a single `<DialogHost>`
- **ModalView** (**Blazor only**, core package — there is **no MAUI equivalent**; never emit `<ModalView>` in XAML, use `FloatingPanel`/`ShinyContentPage` or `IDialogService` there): a **modal window** — a titled panel over a backdrop that owns the screen until dismissed. Declarative, unlike `Dialogs`: you write the content and it hosts it, which is what makes it the answer for a form/editor/picker rather than alert-confirm-prompt. Every region is optional and replaceable — `Title`/`Subtitle`/`Icon` **or** `HeaderTemplate` (which keeps the header bar so the close button still has a home) **or** `ShowHeader="false"`; the built-in ✕ **or** `CloseButtonTemplate` **or** `ShowCloseButton="false"`; `FooterTemplate` **or** a `Buttons` list of `ModalButton` (rendered as `ShinyButton`, each with `Type`/`Appearance`/`Icon`/`State`/`Disabled`/`ClosesModal`/awaited `OnClick`/`Tag`). State is two-way `IsOpen` plus `ShowAsync`/`CloseAsync`/`ToggleAsync`; events are `Opened`, cancellable `Closing` (`ModalClosingEventArgs.Cancel` — the dirty-form veto, skipped only when `IsOpen` is bound to false) and `Closed(ModalCloseReason)` carrying `CloseButton`/`Backdrop`/`Escape`/`Button`/`Programmatic`. Sizing is `Size` (`Small`/`Medium`/`Large`/`ExtraLarge`/`Full`, caps not fixed widths) or explicit `Width`/`Height`/`Max*`, with `Placement` `Center`/`Top`/`Bottom`, six `ModalAnimation`s and a `ShowBackdrop`/`BackdropOpacity`/`BlurBackdrop` backdrop. It behaves as a **window** when asked: `Draggable` by the header, `Resizable` from a corner grip, `AllowMaximize`/`ShowMaximizeButton`/`MaximizeOnHeaderDoubleClick`. The modal contract is not optional and comes for free: focus moves in (`data-shiny-autofocus` to choose where) and is trapped, page scroll locks without a layout jump, focus is restored, `role="dialog" aria-modal="true"`, and modals stack with only the topmost answering Escape. No host component and no `AddShinyModal()` — it renders where you put it, so keep it at page level, out of any ancestor with `transform`/`filter`/`contain`. See modal.md
- **Captcha** (**Blazor only**, core package — there is **no MAUI equivalent**; never emit `<Captcha>` in XAML and never call a `UseShinyCaptcha()` on `MauiAppBuilder`): a human check in front of a form — one `<Captcha />` over four providers selected **by name**: the built-in `local` challenge (no account, no site key, no third-party script, works offline and inside a `BlazorWebView`), plus Google `recaptcha`, `hcaptcha` and Cloudflare `turnstile`. Registration is optional — with nothing registered the local challenge renders — and is done with `AddShinyCaptcha(c => …)` or the umbrella's `cfg.ConfigureCaptcha(c => …)`, taking `UseLocal(configure?, name?)` / `UseReCaptcha(siteKey)` / `UseHCaptcha(siteKey)` / `UseTurnstile(siteKey)` / `UseProvider<T>()`, plus `SetDefaultProvider`/`SetTheme`/`SetSize`/`SetLanguage`; the component resolves `Provider`, then the configured default, then the first registered, then local, and a name that is registered nowhere renders a visible "no provider" alert rather than silently weakening the check. **The site key is public and the secret key never leaves your server**: the component yields a *token* in `State.Response`, and it is the server that posts it to the provider's siteverify — so pair it with the `Validate` callback (called with the fresh token the moment the widget solves; returning false, or throwing, keeps the state invalid and by default resets the challenge) and treat `IsSolved` alone as untrusted. Gate a submit button on the `ValidChanged` event, which flips back when a solved challenge expires. `Size="CaptchaSize.Invisible"` scores the session in the background and needs `ExecuteAsync()` from your submit handler (`Flexible` is Turnstile-only; `BadgePosition` places the badge). `LocalCaptchaOptions` covers `Mode` (`Text` canvas / `Math`, which renders real text a screen reader can read), `Length`, `CharacterSet`, `CaseSensitive`, `Width`/`Height`, `ExpirySeconds`, `MaxAttempts` and all the wording — but say plainly that the local challenge is generated *and checked* in the browser, so it is a bot speed bump, not a security boundary. Other members: `State` (`Valid`/`Response`/`ProviderName`), `Response`, `ResetAsync()` (call it after a failed submit — a token is single-use), and events `Solved`/`Expired`/`Errored`. Extend with `RemoteCaptchaProvider` + a `RemoteCaptchaDescriptor` for another script-and-global widget, or `ICaptchaProvider` for anything else. See captcha.md

- **TextEntry**: A Material Design-inspired text entry control with animated floating placeholder, customizable border, left/right tool slots, hint text for validation, character count, read-only/password modes, and reusable tools (ClearButtonTool, TextEntrySpeechToTextTool)
- **Slider**: A slider control with a two-color gradient track, blended thumb border that samples the gradient at the current position, tooltip with custom templates, and full drag/tap interaction
- **RangeSlider**: A two-thumb variant of Slider selecting a lower/upper value pair (`LowerValue`/`UpperValue`). Reuses the gradient (shown across the active segment between thumbs), blended thumb borders, and per-thumb tooltips, and adds `MinimumRange` (hard-stop gap) and `MaximumRange` (pushes the other thumb) constraints. See range-slider.md
- **ProgressBar**: A progress bar with gradient fill and a Vista-style shimmer pulse that sweeps left-to-right. Configurable `PulseLength` (width of sheen) and `PulseSpeed` (sweep duration). Triggers on value change or timed interval. Supports indeterminate mode and text overlay. The fill **slides** to each new value rather than snapping, symmetrically - a value that drops drains back at the same rate it filled - via `AnimateProgress`/`ProgressAnimationDuration`/`ProgressAnimationEasing`; only a value change animates, while a width change driven by layout snaps. See progressbar.md
- **ProgressLine** (both hosts, **core** packages): The thin line that runs across the **top or bottom of the window** while something loads. A sibling of `ProgressBar`, not a mode of it - `ProgressBar` fills a slot you gave it in a layout, `ProgressLine` is chrome with no slot that pins itself to a page edge and knows about the navigation bar, tab bar and safe area so it lands *against* them rather than under them. It composes a `ProgressBar` internally, so the gradient, shimmer, indeterminate sweep and animated fill are the same code. On **MAUI** a line declared anywhere in a page's markup **removes itself from that layout** and installs into the page's overlay layer on the edge named by `Position` (`Dock="False"` keeps it inline). The inset follows one rule - *a bar earns an offset exactly when it is painted inside the same coordinate space the line is* - so a `ShinyTabBar` docked over a Shell page pushes the line up by its measured height, while `ShinyNavigationPage`, `ShinyTabbedPage`, native `NavigationPage` and native `TabbedPage` all resolve to zero because the page's content area already excludes their chrome. On **Blazor** it is `position: fixed` with `env(safe-area-inset-*)`, plus `Anchor="Container"` to run along a panel's edge instead of the window's and a `--shiny-progressline-offset` custom property any ancestor can set. Bind **`IsActive`** (animated), never `IsVisible`. Also driven from code with no markup via `IProgressLineService` (`UseShinyControls()` on MAUI; `AddShinyControls()` plus one `<ProgressLineHost />` on Blazor, registered **Scoped**): `using var run = svc.Start(c => c.BarColor = ...); run.SetProgress(0.6); run.Complete();`. Runs are **reference-counted** so two overlapping operations are one line that stays up until the slower lands, the **slowest** run is the one shown (not the average), progress never goes backwards, and with nothing reported the line **trickles** - decelerating toward `TrickleCeiling` (0.9) and never arriving, because a line that reaches 100% on its own has claimed the work finished. `Indeterminate = true` sweeps instead. MAUI re-resolves the showing page each tick, so a run started before a navigation follows it. See progressline.md
- **Overlay & LoadingOverlay**: Full-screen overlay with configurable backdrop color and opacity, fade animation, and custom content via `DataTemplate` (MAUI) or `RenderFragment` (Blazor). `LoadingOverlay` extends it with built-in spinner (indeterminate) or progress bar (determinate) plus optional message text. On MAUI it also carries `ContentAlignment` (`Start`/`Center`/`End`, default Center) + `ContentMargin` for content that should sit off-centre — a prompt bar near the top rather than a dialog in the middle — and `ShowEdgeGlow` + `GlowOptions`, which rim the page with the Siri-style animated colour wash for as long as the overlay is up (behind the content, in front of the backdrop, click-through). Quick entry's in-app presentation is built on exactly these rather than its own scrim, so it shares the page's `OverlayHost` backdrop with everything else instead of stacking a second one. A `DataTemplate` that returns the **same** view instance each time is supported, which is how you host one long-lived view rather than rebuilding it per show
- **SkeletonView**: A content-wrapping control (similar to `RefreshView`) that shows animated shimmer placeholders while `IsBusy` is true, then reveals the real content when loading finishes. Built-in line placeholders (configurable `ItemCount`/`ItemHeight`/`ItemSpacing`/`CornerRadius`/`BaseColor`/`ShimmerColor`) or a custom placeholder layout via `SkeletonTemplate` (MAUI) / `SkeletonContent` (Blazor). Shimmer is a sweeping `LinearGradientBrush` band on MAUI and an animated CSS gradient (honoring `prefers-reduced-motion`) on Blazor. Use it for inline content regions; use `LoadingOverlay` for whole-page loading
- **StateView & Wizard** (both hosts, **core** packages): `StateView` shows exactly one of several named branches chosen by a string - the declarative form of the `IsVisible` / `@if/else` ladder. States are `StateViewState` (MAUI `ContentProperty` is `Content`; Blazor renders nothing itself and hands its `ChildContent` to the host, so an unreached branch is never built). Matching is ordinal and case-insensitive, and an unmatched name falls back to `DefaultState` then the first declared state, so a typo shows something rather than a blank rectangle. MAUI adds a lazy `ContentTemplate` (built on first show, cached unless `CacheContent="False"`). `Transition` is `None`/`Fade`/`Slide` (direction taken from the move)/`SlideLeft|Right|Up|Down`/`Scale`. `Wizard` builds on the same model: `WizardStep` **is** a `StateViewState` plus rules. Validity gates are layered cheapest-first - `IsValid`, `IsOptional`, `ValidateCommand` (MAUI, runs *before* `IsValid` is read so a command that sets the flag is enough), `Validate` (Blazor, `Func<Task<bool>>`, so a server round-trip is a first-class validator), then a cancellable `StepChanging`. `IsVisible="False"` takes a step out of the run entirely - skipped by Next/Back, dropped from the indicator, excluded from `StepCount` - which is how a conditional branch is modelled; `IsEnabled="False"` leaves it drawn but unreachable. The default indicator is the **pointed breadcrumb** (`ProgressStyle="Chevron"`, plus `Dots`/`Bar`/`None`, or `Progress` to replace it wholesale) - drawn on a `GraphicsView` on MAUI so it renders on every head including AppKit and GTK4, and a CSS `clip-path` on Blazor. The wizard owns `GoNextCommand`/`GoBackCommand`/`FinishCommand`/`CancelCommand`/`GoToStepCommand` (MAUI) and `GoNextAsync`/`GoBackAsync`/`FinishAsync`/`CancelAsync`/`GoToAsync` (Blazor), so a button inside a step navigates without the view-model re-implementing it; `CanGoBack`/`CanGoNext` stay yours and are ANDed with the wizard's own checks. `Finishing` is cancellable so a submit rejected server-side leaves the user on the last step. See wizard.md

- **Walkthrough & Tooltip** (both hosts, **core** packages): `Walkthrough` is a guided tour - dim the page, cut an animated spotlight around one control at a time, say what it does. **Steps are declared together on the walkthrough, in order** (`Steps` is the content property), *not* attached to the controls they describe: on a real screen attached ordering scatters the sequence where nothing can see it whole, so reordering means hunting and a conditionally-hidden control derails the rest silently. `IsVisible="False"` takes a step out of the run and re-numbers the counter. A step **advances three ways** - the Next command (`NextCommand`/`NextAsync`, or the built-in nav row), tapping the highlighted control itself (`AdvanceOnTargetTap`/`AdvanceOnTargetClick`, which implies `AllowTargetInteraction`), or a dwell timer (`Duration` in ms; `DurationIn`/`DurationOut` are the *animation* lengths, which is the one naming trap). Four displays: `Popover` (card + tail, the default), `Tooltip` (compact, no buttons), `Inline` (card, no tail), `Spotlight` (no card - text on the dim; needs `UseOverlay` and falls back to `Popover` without it). `RememberRunKey` is what makes onboarding run once - backed by `IWalkthroughStore` (Preferences on MAUI, `localStorage` on Blazor via `AddShinyWalkthrough()`, both replaceable); `Restart()` clears it and re-runs, which is the "show me the tour again" menu item. `AllowTargetInteraction` fences the backdrop with four panels *around* the hole rather than one catcher, because hit testing has no notion of a hole. Targets are `{x:Reference}` on MAUI (prefer it - compile-checked) or a CSS selector on Blazor. `Tooltip` is the bubble underneath it, usable on its own: it either **wraps** its target or **points at one** by reference/selector, opens on `Manual`/`Tap`/`LongPress`/`Hover`/`Focus`, and is bound with **`IsOpen`** - never `IsVisible`, which is `VisualElement.IsVisible` and would hide the anchor. Placement is a preference, not a promise: a side with no room flips to its opposite, the bubble is clamped inside `ScreenMargin`, and the tail slides along its edge to keep pointing at the target it was moved away from. The bubble is drawn in a page-level layer (MAUI) or the browser's top layer (Blazor), so it is never clipped by a scroll view or card. See walkthrough.md and tooltip.md
- **Keyframe** (MAUI only, separate `Shiny.Maui.Controls.Keyframe` package): Declarative keyframe animation — the CSS `@keyframes` model in XAML (`kf:Animate.Keyframes` attached property with `Keyframes`/`Track`/`Key`, `Duration`/`Delay`/`Iterations`/`Direction`/`Fill`/`Speed`/`AutoPlay`), plus a fluent C# `TimelineBuilder` and composable `Storyboard` (`Add`/`Then`/`With`/`Stagger`). Evaluation is a pure function of time, so animations **seek** — scrub from a Slider or gesture via `Player.SeekProgress`, reverse mid-flight with a negative `Rate`, and export deterministically — which is exactly what MAUI's own fire-and-forget `Animation.Commit` cannot do. `Easing` takes named curves (`CubicOut`, `BounceOut`, `Emphasized`, …) **and** CSS function syntax (`cubic-bezier(...)`, `steps(n)`, `spring(...)`) so design-tool curves paste in verbatim; omitting `Value` on a key resolves it to the target's live value at start. Properties resolve through an explicit hand-registered `AnimatableProperties` registry (AOT-safe, no reflection) covering transforms/opacity/colors/layout, extensible via `AnimatableProperties.Register`. `KeyframeView` renders a `KeyframeScene` layer tree (rectangle/ellipse/path/text/image layers) to a canvas with two-way bindable `Progress`. Optional `Shiny.Maui.Controls.Keyframe.Export` adds headless deterministic frame export and a pure-managed GIF encoder — the only piece that pulls SkiaSharp. See keyframe.md
- **Motion Icons** (both hosts, in the **core** packages; artwork lives in `Shiny.Controls.MotionIcons.Shared`): 111 animated icons that run on a timer, on hover, on tap, when they scroll into view, or on command — `MotionIconView` on MAUI (a `GraphicsView`, so it works on every head including AppKit and GTK4) and `<MotionIcon>` on Blazor. `Trigger` is a `[Flags]` enum (`Manual`/`Loop`/`Hover`/`Press`/`Appear`, default `Hover|Press`); hover **finishes the cycle in progress** rather than snapping back mid-pose, and `Interval` is folded into the animation itself so a resting gap needs no timer on either host. Bind `IsPlaying` for a busy state. Each icon carries motion drawn for it (a bell rings from its crown, a hamburger morphs into a cross, a tick draws itself on), overridable with generic presets — `Pulse`/`Beat`/`Spin`/`Shake`/`Wobble`/`Bounce`/`Float`/`Pop`/`Tada`/`Flip`/`Swing`/`Blink`/`Draw`/`Nudge`/`Jiggle` — that also work on your own artwork via `PathData`, a `MotionIconDefinition`, or `MotionIconLibrary.Register` (which can also replace a built-in app-wide). **Both hosts compile the same `MotionSpec` into different machinery**: MAUI draws it on a canvas ticked by one shared timer per window; Blazor compiles it to `@keyframes` once and lets the browser composite it with no C# running per frame. Blazor's `Color` defaults to `currentColor` so icons inherit surrounding text colour including disabled states. **Write path data with explicit `L` commands** — `Microsoft.Maui.Graphics` does not implement SVG's implicit lineto (`M6 6 18 18` becomes two movetos) nor run-together decimals (`l.06.06`), both of which browsers accept, so design-tool artwork can look perfect on Blazor and draw nothing on MAUI. See motion-icons.md
- **MediaElement** (separate `Shiny.Maui.Controls.MediaElement` / `Shiny.Blazor.Controls.MediaElement` packages, plus `Shiny.Maui.Controls.MediaElement.Linux` for the GTK4 head): Local and remote audio/video on iOS, Android, Windows, macOS AppKit, Linux GTK4 and Blazor — AVPlayer, Media3/ExoPlayer, Windows.Media.Playback, GtkMediaFile and HTML5 media behind one API. The transport bar is **drawn by Shiny rather than handed to the platform**, which is the only way each piece can toggle independently (`ShowPlayPauseButton` / `ShowSeekBar` / `ShowVolumeControl` / `ShowFullScreenButton` / `ShowTimeLabels` / `ShowPictureInPictureButton`): native transport UI is all-or-nothing everywhere but Windows. Commands on MAUI (`PlayCommand`, `PauseCommand`, `StopCommand`, `SeekCommand`, `MuteCommand`, `ToggleFullScreenCommand`, `PictureInPictureCommand`) and methods on Blazor. An `IMediaPlayerBackend` owns the player and the *view* is pushed into it, so fullscreen hands the same stream to a second surface (no re-buffering) and backgrounding detaches the video surface while audio keeps running. **Background audio** with OS lock-screen/notification controls (`EnableBackgroundPlayback` + `Metadata`) and **Picture-in-Picture**, both needing an app-level manifest opt-in the library cannot add. `Capabilities` reports what the current backend actually honours so a UI never offers a dead control. See mediaelement.md
- **SplashScreen** (Blazor only): A boot splash that is on screen **before Blazor starts** — so it deliberately is *not* a Razor component. Ships as a classic (non-module) `splash.js` exposing a global `shinySplash`, an unscoped `shiny-splash.css`, and a managed side (`ISplashScreen` + `<SplashScreenHost />`, registered with `AddShinySplashScreen()`) that only owns status, progress, and the handoff to the app. The host `<div id="shiny-splash">` **must sit outside `#app`** (Blazor clears `#app` when it attaches the root component, long before the app is ready) and `splash.js` must be referenced **before** `blazor.webassembly.js`/`blazor.webview.js`. Three tiers of customization: `data-*` attributes, a `shinySplash.show({...})` config object, or your own arbitrary HTML inside the host (the script then only binds `[data-shiny-splash-status]` / `[data-shiny-splash-progress-fill]` / `[data-shiny-splash-percent]` and owns fade/hide). Spinners `ring`/`dots`/`bar`/`pulse`/`none`, `minDurationMs` anti-flicker, a `failSafeMs` auto-dismiss so a boot failure is never hidden behind the splash, and WASM-only `blazorLoadProgress` mirroring `--blazor-load-percentage`. `<SplashScreenHost Until="StartupAsync" />` awaits real startup work and dismisses in a `finally`. **No MAUI equivalent by design** — MAUI has the native `MauiSplashScreen`; in Blazor Hybrid you use both and match their background colors. See splash-screen.md
- **CarouselGallery**: Netflix-style horizontal carousel with snap-to-center, configurable scale transforms (FocusedItemScale/UnfocusedItemScale), peek area insets, infinite loop, two-way position tracking, and `SnapCount` (0=free scroll, 1+=snap to item). Uses native recycler views on MAUI and CSS scroll-snap on Blazor
- **Carousel** (Blazor only, `Carousel<TItem>`): An Embla-style, transform-based drag carousel. A pointer-drag engine (mouse click-drag + touch flick with momentum) translates the track — no native scroll-snap. Parameters: `Items`/`ItemTemplate` (+`PlaceholderTemplate`, `EmptyTemplate`, `ThumbnailTemplate`); layout `Align` (Start/Center/End), `SlidesPerView` (fill-to-fit sizing), `SlidesToScroll`, `VariableWidths` (content-sized slides), fixed `ItemWidth`/`ItemHeight`/`ItemSpacing`, `Orientation` (Horizontal/Vertical) with `ViewportHeight`, `Rtl`; interaction `Draggable`, `DragFree` (momentum, no snap), `Loop`; auto-motion `AutoPlay`+`AutoPlayInterval`, `AutoScroll`+`AutoScrollSpeed` (continuous marquee), `PauseOnHover`; scroll-linked `Effect` (None/Scale/Opacity/Parallax/Fade) tuned by `FocusedItemScale`/`UnfocusedItemScale`/`ParallaxFactor`/`MinOpacity`; chrome `ShowArrows`/`ShowIndicators` (dots = snap count)/`ShowCounter` ("n / total")/`ShowProgress` (scroll-position bar)/`ShowThumbnails`; `LazyLoad`+`LazyLoadBuffer`. Two-way `CurrentPosition` (selected snap index), `CurrentPositionChanged`, `ItemSelected`. Methods `NextAsync`/`PreviousAsync`/`GoToAsync(snap)`/`GoToSlideAsync(item)`. Keyboard arrows/Home/End. Effects animate live during drag. Use `CarouselGallery` for MAUI/Blazor parity; use `Carousel` for the richer Blazor-only drag experience
- **ParallaxCollectionView** (MAUI) / **ParallaxList** (Blazor): A scrollable list with a hero header that translates at a configurable fraction of the scroll offset (`ParallaxFactor`, default 0.5 = half speed). Optional `CollapseToSticky` clamps the header to a `MinHeaderHeight` minimum, and `FadeHeaderOnScroll` fades it out as it scrolls. MAUI wraps a real `CollectionView` (`ItemTemplate`, `EmptyView`, `SelectionMode`, `SelectedItem`, `ScrollTo`, custom `ItemsLayout`) in a `Grid` and drives the hero translation from `CollectionView.Scrolled`. Blazor uses a CSS-positioned hero plus a tiny JS scroll listener that mutates `transform`/`opacity` directly (rAF-throttled) so parallax runs at native scroll framerate without Razor re-renders. Both hosts fire a `Scrolled` event with `ParallaxScrollEventArgs(verticalOffset, headerTranslation, headerVisibleHeight)` for driving sticky titles, fading nav chrome, etc. No platform handlers
- **ShinyToolbar + ShinyTabBar** (**Blazor only** — there is no MAUI equivalent, never emit `shiny:ShinyToolbar`): `ShinyToolbar` is an action bar docked to the top or bottom of its scroll container (`Dock`, `Sticky`, `Title`/`StartContent`/`ChildContent`, `Frosted` glass, `ShowItemLabels`); its trailing `ToolbarItem`s are measured against the room the bar has and the ones that do not fit **collapse into an overflow dropdown** automatically, while `EndContent` sits beside them pinned and never collapses. Any item with `Children` becomes a **menu button** — it opens a dropdown instead of raising `ItemClicked`, nests into submenus as deep as you like, and takes `IsSeparator` dividers between groups; a menu button that lands in the overflow menu keeps its children as a submenu row there. Every panel is drawn in the browser's **top layer** via the popover API, so a bar inside a card, panel or scroller is never clipped by that ancestor's overflow. `ShinyTabBar` is the mobile-style bottom tab bar with `@bind-SelectedKey`, per-tab `ActiveIcon` and badges. See toolbar-tabbar.md

- **Flyout** (**MAUI only** — `using Shiny.Maui.Controls.Flyout;`; the Blazor equivalent is `AppLayoutPanel` inside `AppLayout`, see layout.md): a side panel that slides in from either edge, rests as a narrow icon rail instead of a full panel, and either **pushes** the content aside or **floats** over it — the `FlyoutPage` replacement that also works **inside Shell**, which `FlyoutPage` cannot. `FlyoutPanel` is one panel (`PanelContent` body, `RailContent` for the rail, pinned `HeaderContent`/`FooterContent`); `FlyoutView` is the layout hosting `Content` plus a `Start` and/or `End` panel; `ShinyFlyoutPage` is a `ShinyContentPage` built around one, where the content property is **`Detail`** (a `View` — MAUI cannot parent a `Page` inside a page, so never emit `<ShinyFlyoutPage.Detail><NavigationPage>`); and `ShinyFlyout.StartTemplate`/`EndTemplate` are attached properties on a Shell or NavigationPage that install a flyout over **every** page it shows (a `DataTemplate`, so each page builds its own panel and only the state carries across; pair with `Shell.FlyoutBehavior="Disabled"`). States are `Hidden`/`Collapsed`(the rail)/`Expanded`, sides are RTL-aware `Start`/`End`, presentation is `Overlay`/`Push`/`Auto` (`Push` at or above `CompactWidth`, default 800). **The rule**: `Presentation` governs an *Expanded* panel — a `Collapsed` rail always insets the content on both presentations, which is why expanding a rail in `Overlay` floats the panel over the content without the content moving. `CollapseBelow` drops an expanded panel to `CollapsedState` under a width measured on the **FlyoutView** and restores it when it grows back, unless the user has since changed the state deliberately. Plus `ExpandedWidth`/`CollapsedWidth` (+`Min`/`MaxExpandedWidth`, `IsResizable` inner-edge drag handle on pushing panels), `HasScrim`/`CloseOnScrimTap`, `IsSwipeEnabled`/`EdgeSwipeWidth` edge-swipe-to-open, `AnimationDuration`, and theme-following `PanelBackgroundColor`/`DividerColor`. Drive it from a view model with `IFlyoutService` (`ToggleAsync`/`SetStateAsync`/`GetState`/`StateChanged`), which resolves the flyout on the page currently showing. See flyout.md

- **ShinyNavigationPage + ShinyNavBar** (**MAUI only**, core package — there is **no Blazor equivalent**; never emit `<ShinyNavigationPage>` in Razor): a `NavigationPage` **subclass**, so `PushAsync`/`PopAsync`/`PopToRootAsync`/`InsertPageBefore`/`RemovePage`, the modal stack, page lifecycle, Android hardware back and `Pushed`/`Popped`/`PoppedToRoot` all still work — what it adds is **toolbar items on the left as well as the right**, which no platform's native bar has room for (that slot is the back button's everywhere, and AppKit/GTK4 have no bar at all). The native bar is hidden and `ShinyNavBar` draws its own, so the overflow menu, badges, motion icons and collapsing large title render identically on every head. Items go on the **page** as `ShinyNav.LeftItems` / `ShinyNav.RightItems`; `NavBarItem` **derives from `ToolbarItem`** (so `Text`/`IconImageSource`/`Command`/`IsEnabled`/`IsDestructive`/`Clicked`/`Order`/`Priority` keep their meanings) and adds `Icon` (motion icon name), `IconSource`/`IconPathData`/`Motion`, `IconColor`, `Badge`/`BadgeColor` (`""` is a dot, `null` is nothing), `Display`, `IsVisible`, `IsSeparator`, `Tag`. A page's own `Page.ToolbarItems` are drawn on the right **automatically**, ahead of `RightItems`, so adopting the page never means rewriting a toolbar. `Order="Secondary"` always overflows; anything past `MaxVisibleItems` (default 3, per side) overflows behind it. Everything MAUI already gives a `NavigationPage` is honoured rather than reinvented — `Page.Title`, `SetHasBackButton`, `SetBackButtonTitle`, `SetTitleView`, `SetTitleIconImageSource`, `SetIconColor`, `BarBackground`/`BarBackgroundColor`/`BarTextColor` — **with one exception**: `SetHasNavigationBar` is read only as the *starting* value (that property is the slot the class had to take over to hide the native bar), so the runtime switch is **`ShinyNav.SetIsNavBarVisible(page, false)`**. `LargeTitleDisplay` is `Inherit`/`None`/`Always`/`Collapsing` — `Inherit` means `None` on the navigation page and "follow the navigation page" on a page, which is what lets one page turn the large title off; `Collapsing` follows the first `ScrollView`/`ItemsView` found unless `ShinyNav.ScrollSource` names one. Per-page overrides live on `ShinyNav` (`Subtitle`, `LargeTitle`, `TitleAlignment`, `BackButtonIcon`, `BackButtonCommand`, `BarBackgroundColor`, `BarTextColor`); appearance defaults live on the navigation page (`BarHeight`, `BarPadding`, `HasShadow`, `HasSeparator`, `ItemSpacing`, `IconSize`, `MaxVisibleItems`, `OverflowIcon`, `MenuTemplate`, `AnimationDuration`, `BarIconColor` — **not** `IconColor`, which is MAUI's attached per-page one). Non-`ContentPage` pages are left alone; a `ShinyContentPage` is wrapped at `PageContent` so overlays stay on top; iOS edge-swipe-back is put back deliberately (`EnableSwipeBackGesture`). Shell cannot host it — present it modally from a Shell app. See navigationpage.md

- **Ribbon** (**both hosts**, but a *desktop* control — `Shiny.Maui.Controls.Desktop` in the `Shiny.Maui.Controls.Desktop.Ribbons` namespace, and the **core** `Shiny.Blazor.Controls` package on Blazor, the same split docking uses): the Office-style tabbed command bar — `Ribbon` → `RibbonTab` → `RibbonGroup` → items, authored declaratively on both hosts (nested elements in XAML, nested components in Razor; there is no `ItemsSource` form, never invent one) and needing **no registration at all** (do not emit `UseShinyRibbon()`/`AddShinyRibbon()`). Items are `RibbonButton`, `RibbonToggleButton` (two-way `IsChecked`/`Checked`), `RibbonSplitButton` (face runs the default action, chevron opens the menu), `RibbonMenuButton`, `RibbonSeparator` and `RibbonContentItem`/`RibbonContent` for arbitrary content. **Nothing declares a column** — a `Large` item takes one to itself and `Small` items stack `SmallItemRows` (3) deep in a shared one, with a separator or a large item starting a fresh column, so a group is re-flowed by reordering its items and nothing else. Dropdown entries are `RibbonMenuEntry` (nestable `Children` fly out as submenus): markup children on MAUI, a `List<RibbonMenuEntry>` `Menu` parameter on Blazor, the same split `ToolbarItem.Children` uses. A **contextual tab** is just `ContextTitle` (captions the band, picks the tertiary accent) plus a bound `IsVisible`/`Visible`; when the showing tab stops being selectable the bar falls back to the nearest one that is. `DisplayMode` (two-way) is `Expanded`/`Collapsed`/`Simplified`, and groups that do not fit fold into single buttons worst-`Priority`-first (`CanCollapse="false"` pins one open, `AllowGroupCollapse="false"` scrolls instead). In Razor `Size` must be qualified — `Size="RibbonItemSize.Small"` — and using `<QuickAccess>` forces `<ChildContent>` to be named too. **Do not generate a ribbon for a phone**; use ShinyToolbar/ShinyTabBar or ShinyTabbedPage. See ribbon.md

- **ShinyTabbedPage + ShinyTabBar** (**MAUI only**, core package — Blazor's `ShinyTabBar` is a *different*, simpler component, see toolbar-tabbar.md; never emit `<ShinyTabbedPage>` in Razor): an improved `TabbedPage` — **motion icons** in the tabs, per-tab **badges**, an animated **transition** between tabs, tab content that is **built on first selection and then cached**, and a raised **centre button** that presents the current page's actions. `ShinyTabbedPage` is the standalone page (content property `Tabs`, holding `ShinyTabItem`s with `Content` or a `ContentTemplate`; `Transition` takes the same `StateTransition` as `StateView`/`Wizard` and `Slide` is direction-aware). A `ContentTemplate` may inflate a `View` **or a whole `ContentPage`**, which is *adopted* — its `Content` is hosted, its `Title` fills in the tab, its `BindingContext` is mirrored across — but it gets **no `OnAppearing`** (MAUI raises page lifecycle only for the page the platform presented, so `SendAppearing()` on it does nothing): implement **`ITabAware`** (`OnTabAppearing`/`OnTabDisappearing`) on the content, the page or its view model instead. For a `Shell`, add **`ShinyTabBarBehavior`** to `Shell.Behaviors` — it hides the platform bar, mirrors the Shell's own tabs, docks the bar over the current page and turns a tap into a `CurrentItem` change, so routes, deep links and per-tab navigation stacks all keep working; the bar's `Items` are managed by the behavior, so per-tab chrome goes on the Shell elements. Per-page chrome is the **`ShinyTabs`** attached properties (`Badge`, `BadgeColor`, `Icon`, `Title`, `Actions`, `MenuContent`/`MenuContentTemplate`, `IsTabBarVisible`) set on the page or the `ShellContent` — `Actions` needs a **`<shiny:TabActionCollection>`** wrapper in XAML, the way `VisualStateGroups` needs `VisualStateGroupList`. `Badge=""` is a **dot**, `Badge=null` is nothing; put the badge on the tab/`ShellContent` when it must show before the tab is ever opened, and on the page when the page computes it. `TabCenterButton.Mode="Menu"` falls back to a plain click when nothing is declared. Indicators are `Pill`/`Line`/`Underline`/`Dot`/`None`; nothing touches a platform SDK, so it renders on AppKit and GTK4 where MAUI's own `TabbedPage` does not. See tabbedpage.md

- **Layout + AppLayout** (**Blazor only** — MAUI already has `VerticalStackLayout`/`HorizontalStackLayout`/`Grid`, so use those there instead): `VStack`/`HStack` are flexbox stacks (`Spacing` in px, `Align`, `Justify`, `Wrap`, `Reverse`, `Grow`, `Scrollable`, `Padding`, `Background`) styled entirely inline, and they *merge* a consumer's `class`/`style` rather than letting the splat clobber their own. `Grid`/`Row`/`Column` are a responsive 12-column grid: each `Column` takes a span per breakpoint (`Xs` &lt;576 · `Sm` ≥576 · `Md` ≥768 · `Lg` ≥992 · `Xl` ≥1200 · `Xxl` ≥1400) that **cascades upwards** Bootstrap-style, so `Md="6"` alone is full width on phones and half from 768px up, plus per-breakpoint `OffsetXs…OffsetXxl` / `OrderXs…OrderXxl`, `Fit` (shrink to content) and span-less columns that share the row equally; spans are chained `var()` fallbacks in media queries rather than 72 generated classes, and gutters are padding-based so a 6+6 pair still totals 100%. `AppLayout` is an application shell — `AppLayoutHeader`, `AppLayoutFooter`, `AppLayoutPanel` (`Side="Left|Right"`) and `AppLayoutContent` placed by CSS grid areas, so markup order does not matter. Panels cycle `PanelState.Hidden`/`Toolbar` (a rail rendering arbitrary `ToolbarContent`)/`Shown` via `@bind-State` or `SetStateAsync`/`ToggleAsync`, drag-resize between `MinSize`/`MaxSize` reporting back through `@bind-Size` (JS writes the width during the drag and only commits on pointer-up), keep their own scroll regions for the body and rail while `HeaderContent`/`FooterContent` stay pinned, take per-region border config that falls back to the shell's `BorderWidth`/`BorderColor` through CSS variables, auto-compact under `CollapseBelow` (measured on the **shell**, not the window) with `Shown` there floating as a scrimmed drawer, and optionally persist state+width to localStorage via `PersistKey`. `HeaderSpan`/`FooterSpan` choose whether the header/footer run the full width or are inset between the panels. See layout.md
- **StaggeredGrid**: Pinterest-style masonry/waterfall layout with variable-height items in configurable columns. Items with HeightRequest on the root template view use that value directly for measurement. Uses native staggered layout managers on MAUI and CSS column-count on Blazor
- **VirtualizedGrid**: Full-featured grouped grid with sticky section headers, virtualization, orientation-aware column counts, cell padding, load-more button (renders as footer at end of data) with custom template support, and item visibility tracking. Uses native grid layouts on MAUI and CSS Grid on Blazor
- **Quick Entry** (`using Shiny.Maui.Controls.QuickEntry;` / `using Shiny.Blazor.Controls.QuickEntry;`) — an assistant-style prompt popup summoned over whatever the user is looking at, in the CORE packages on BOTH hosts, plus a Siri-style screen-edge glow on the same service. Registered by `UseShinyControls()` / `AddShinyControls()`; configure with `cfg.ConfigureQuickEntry(o => …)`. Blazor additionally needs one `<QuickEntryHost />` in the root layout. **Presentation**: `QuickEntryOptions.Presentation` is `Auto` (default) / `InApp` / `Desktop` — Auto uses a native always-on-top OS window where the `Shiny.Maui.Controls.Desktop` add-on is registered on Windows/macOS/Linux, and an in-app page overlay everywhere else; explicit `Desktop` where unavailable falls back to the overlay and logs. ⚠️ The in-app overlay does NOT paint on the macOS AppKit head (`net10.0-macos`) — re-parenting a ContentPage's Content at runtime does not re-render there, which is a pre-existing limitation shared with Toast and Dialogs, not a quick entry bug; `Auto` picks the desktop window on macOS so the default path is fine, but never suggest `Presentation = InApp` as a fix on that head. **iOS, Android, Mac Catalyst and Blazor are in-app only** — never emit `Presentation = Desktop` as the fix for those, and the Blazor options class has no `Presentation` member at all. `IQuickEntryService`: `Show`/`Hide`/`Toggle`, `PreloadAsync` (MAUI), `Resize` (MAUI), `IsOpen`/`Content`/`ResolvedPresentation`, `Opened`/`Closed`, and the glow — `ShowGlow`/`HideGlow`/`PulseGlowAsync`/`IsGlowVisible`/`IsGlowSupported` (one service, deliberately, since the two are almost always used together). `QuickEntryOptions`: `Presentation`, `Placement` (`TopCenter`/`BottomCenter`/`Center`/`NearCursor`/`Manual` — Blazor has only the first three), `Width`, `CollapsedHeight`, `MaxHeight`, `TopMarginRatio`/`BottomMarginRatio`, `AutoSize`, `DismissOnFocusLost`, `DismissOnScrimTap`, `DismissOnEscape`, `ActivateOnShow`, `ScrimColor`, `ContentFactory`, `RecreateContentOnShow`, `ScreenGlow` (`None`/`WhileOpen`/`WhileBusy`), `Glow`, and the desktop-only `HotKey`/`ShowInTaskbar`/`JoinAllSpaces`. The default content is **`PromptView`** (NOT `AiPromptView` — that name never shipped): `Text`, `Placeholder`, leading icon (`Icon`/`IconContent`/`ShowIcon`/`IconSize`, default is the built-in animated orb), `IsBusy`, `BusyText`, `Suggestions` (`PromptSuggestion` = Text/Description/Glyph/Value) + `SuggestionTemplate`, `MaxVisibleSuggestions`, `DropdownContent`, `DropdownHeight` (unset = size to content, set = pinned and scrolling), `Response` (plain text, rendered in a built-in label) and `ResponseContent` (any view/markup, wins when both are set), `Footer`, `LeadingTools`/`TrailingTools`, `SubmitCommand`/`SuggestionCommand`/`MicrophoneCommand`, `ShowMicrophone`, `ShowSubmitButton`, `ClearOnSubmit`, colours, events `Submitted`/`SuggestionSelected`/`Cancelled`/`ResponseChanged`. **Tools**: `LeadingTools` (beside the orb) and `TrailingTools` (before the microphone/submit glyphs) take `PromptTool`s — same `IconTextTool` base as `TextEntryTool` on MAUI, a plain object with `Icon`/`Text`/`Title`/`ToolColor`/`Clicked` on Blazor. A tool that needs the prompt implements `IPromptAwareTool` (MAUI `Attach`/`Detach`) or overrides `OnAttached`/`OnDetached` (Blazor, which also hands it the app's `IServiceProvider`); whatever it subscribes to in attach must come off in detach. `PromptTextToSpeechTool` in `Shiny.Maui.Controls.SpeechAddins` / `Shiny.Blazor.Controls.SpeechAddins` reads the answer aloud via `Shiny.Speech` — hidden until there is something to read, a stop button while speaking, with `AutoSpeak`/`HideWhenEmpty`/`TextSelector`/`SpeechRate`/`Pitch`/`Volume`/`VoiceName`/`Culture`. It speaks `Response`, so an answer set only through `ResponseContent` needs a `TextSelector`. MAUI needs `AddSpeechServices()`/`AddTextToSpeech()` registered — every SpeechAddins tool resolves its engine from DI and silently no-ops otherwise. `Shiny.Maui.Controls.SpeechAddins` targets ios/android/maccatalyst/**macos**/windows; there is NO plain `net10.0` target, so the GTK/Linux head cannot reference it (Shiny.Speech's net10.0 assembly is browser-only) — resolve the tool by name in source shared with that head. Blazor needs only a WebAssembly host; the synthesiser is the browser's own `speechSynthesis`, reached through the package's JS module, and nothing needs registering. **It does no AI itself** — handle `Submitted`, set `IsBusy`, assign the response. It is an ordinary control, so it also works inline on a page on every platform. Keyboard: ↑/↓ walk suggestions, Enter submits or picks, Escape unwinds one state at a time (cancel → highlight → response → text) before closing. Custom popup content joins in via `IQuickEntryKeyHandler`, reports busy state for the glow via `IQuickEntryBusyState`, hears open/close via `IQuickEntryPresentationAware`, and — if it changes size — reports its own height via `IQuickEntryAutoSize` (the host otherwise measures, which only holds for fixed-height content). On Blazor the popup's own prompt is configured through `IQuickEntryService.ConfigurePrompt(state => …)` because a service cannot hand a component parameters; using `<PromptView>` directly on a page takes ordinary parameters instead
- **Desktop (Tray Icon + Docking + Desktop Quick Entry)**: A single desktop-only add-on package `Shiny.Maui.Controls.Desktop` bundles the features that share the desktop TFM matrix (Windows + macOS AppKit + MacCatalyst + Linux):
  - **Tray Icon** (`using Shiny.Maui.Controls.Desktop.TrayIcon;`) — cross-platform system tray / status-bar icon. Windows (`Shell_NotifyIcon`), macOS AppKit (`NSStatusItem`, native `net10.0-macos` build), MacCatalyst (AppKit bridged via the Objective-C runtime), Linux (`libayatana-appindicator3` + GTK 3 — requires the system library installed). API: `ITrayIconFactory` resolved from DI, then `ITrayIcon` with `SetIcon(Func<Stream>)` (PNG or ICO bytes — Windows auto-wraps PNG into ICO), `Tooltip`, `Title` (macOS/Linux label, ignored on Windows), `IsVisible`, `IsTemplateImage` (macOS auto-tint), `SetMenu(TrayMenu)`, `ShowMenu()`, and `PrimaryClick`/`SecondaryClick`/`DoubleClick` events. Menus are built fluently with `TrayMenu.Build(b => b.Item(...).Check(...).Separator().Submenu(...))` — `TrayMenuItem`, `TrayCheckMenuItem`, `TraySeparator`, and `TraySubmenu`. Mutating any item's `Label`/`IsEnabled`/`IsVisible` rebuilds the native menu automatically. No Blazor equivalent — tray icons are a desktop OS concept. Registered with `.UseTrayIcon()` in `MauiProgram.cs`
  - **Docking** (`using Shiny.Maui.Controls.Desktop.Docking;`) — Visual-Studio-style window docking for MAUI desktop, with the Blazor host shipping in the main `Shiny.Blazor.Controls` package (namespace `Shiny.Blazor.Controls.Docking`). `DockHostView` attaches to any existing `ContentPage` (not a `ContentPage` subclass) and orchestrates `DockGroupView`, `DockTabStrip`, and `DockSplitter` building blocks. Public surface includes `IDockHost` (per-window controller — `LoadAsync`, `Snapshot`, `ShowPanelAsync`/`HidePanelAsync`/`ActivatePanelAsync`, `ResetLayoutAsync`, `SetRailCollapsedAsync`, `IsLocked`, `Events`; implemented directly by `DockHostView` / `<DockHost>`), `IDockableContent` (optional interface on panel views — per-instance `Title`/`Icon`, `CanClose`/`CanFloat`, `OnActivated`/`OnDeactivated`, `WantsPointerDown` for embedded editors), `IDockableContentFactory` (async `Task<View> CreateAsync(string instanceId, ...)` + `DisplayName`/`Icon`/`CanClose`, registered with `.AddDockPanel<TView>("panel-id", displayName: …, icon: …, canClose: …)`; `canClose: false` hides the tab's close button and makes `HidePanelAsync` refuse the panel, for panels a surface cannot function without), `IDockLayoutStore` (bring-your-own persistence — no default ships; attach via the host's `LayoutStore` property for auto-load at startup + debounced auto-save), `IDockLayoutMigrator` (forward-only schema migrations), `IDockEvents` (`LayoutChanged`, `PanelActivated`, `DragStarted/Completed/Cancelled`), and `IDockCommandScope` (scopes Ctrl+W, Ctrl+Tab MRU, Ctrl+Alt+PgUp/Dn to the dock surface). The layout schema is a pure POCO tree (`DockRoot`, `DockWindowState`, `DockSplit`, `DockGroup`, `DockTab`, `DockEmpty`, `DockCollapsedPanel`) with a source-generated `System.Text.Json` context and `SchemaVersion` + `MinReadableVersion` for migration; `DockSerialization.Serialize`/`Deserialize` round-trip layouts to JSON. Fully interactive: tab drag to merge (drop center) / split (drop edge) / reorder (drop in tab strip) / tear off floating windows (drop outside the host — movable, resizable, re-dockable), draggable splitters with persisted clamped ratios, per-panel collapse to slim edge bars (restore on click; whole rails via `SetRailCollapsedAsync`), locked/read-only mode, and persisted floating-window bounds + collapsed state. Registered with `.UseShinyDocking()` + `.AddDockPanel<TView>("id")` on MAUI, `services.AddShinyDocking() + .AddDockPanel<TComponent>("id")` on Blazor; host controls accept `InitialLayout`, `LayoutStore`, and `IsLocked`
  - **Desktop Quick Entry** (`using Shiny.Maui.Controls.Desktop.QuickEntry;`) — `.UseDesktopQuickEntry()` adds the *native-window* presentation of the core Quick Entry popup (borderless, always-on-top, opening over other applications), the whole-display screen glow, and `IGlobalHotKeyService`. The popup, `PromptView` and the in-app presentation are in the CORE package — see the Quick Entry entry below. Safe to call unconditionally; on MacCatalyst and non-desktop runtimes the presenters report themselves unsupported and the core service stays with the overlay. `IGlobalHotKeyService.Register(accelerator, action)` returns `IDisposable?` — **null means could not claim, which is a normal outcome; never throw or assume success** — and needs at least one modifier. Backed by `RegisterHotKey` (Windows), Carbon `RegisterEventHotKey` (macOS, no Accessibility prompt), `XGrabKey` (Linux/X11) and the `org.freedesktop.portal.GlobalShortcuts` portal (Linux/Wayland, GNOME 45+/KDE 6+, shows a user confirmation and may bind a different trigger). Under Wayland the desktop popup cannot be positioned or raised by the client and the whole-display glow is unavailable
  - **File Drop** (`using Shiny.Maui.Controls.Desktop.FileDrop;`) — `.UseFileDrop()` / `.UseFileDrop<TDelegate>()` registers `IFileDropService`: files dragged from Finder / Explorer / Files onto the application **window**, anywhere in it, **including on top of a `BlazorWebView`**. This is the answer whenever someone asks for app-wide file drag/drop on desktop — do NOT reach for `DropGestureRecognizer`, which is per-view, unimplemented on the AppKit and GTK4 heads, broken on Mac Catalyst (dotnet/maui#23627) and sits behind hosted web content. Windows (WinUI XAML drop on the window root), macOS AppKit (the drop view becomes the window's `contentView`, since AppKit walks *up* the superview chain to find a destination), Mac Catalyst (`UIDropInteraction` on the `UIWindow`, staging `NSItemProvider`s to temp), Linux/GTK4 (`GtkDropTarget` on the toplevel in the **capture** phase). Elsewhere `IsSupported` is false, attaching no-ops and nothing fires — never `#if` around it. `IFileDropService`: `IsSupported`, `IsEnabled`, `Options`, `DragEnter`/`DragOver`/`DragLeave`/`Dropped`, `AttachTo(Window)`. `FileDropOptions`: `AllowedExtensions` (with or without the dot; empty = all), `MaxFileSize`, `MaxFiles`, `AllowDirectories`, `SuppressWebViewDrop` (default true — the switch that makes it work over web content), `AutoAttachWindows` (default true). `DroppedFile`: `FileName`, `FullPath`, `Length`, `Extension`, `ContentType`, `IsDirectory`, `OpenReadAsync()`, `ReadAllBytesAsync()`, plus the static `FromPath`. `IFileDropDelegate` (singleton, `OnFilesDropped(FileDropContext)`) handles drops app-wide and can set `Handled` to suppress the event. ⚠️ **A wholly refused drop raises `DragLeave`, not `Dropped`** — no platform sends a leave after a drop, so an overlay bound to the drag state would otherwise never come down. ⚠️ Hover events may carry placeholder files with no name or size (Catalyst, and every browser); bind overlays to `Files.Count`/`HasAcceptableFiles`. The Blazor half lives in the CORE package — see the Blazor File Drop entry below. Full reference: `file-drop.md`
  - **File Drop** (`using Shiny.Blazor.Controls.FileDrop;`) — **Blazor half of the MAUI service above, in the CORE `Shiny.Blazor.Controls` package.** `services.AddShinyFileDrop(o => …)` / `AddShinyFileDrop<TDelegate>()`, covered by `AddShinyControls()` with `cfg.ConfigureFileDrop(…)`, then ONE `<FileDropHost />` in the root layout (it calls `StartAsync()` after the first render — a JS module cannot be imported during prerender). Listeners go on `window` in the **capture** phase, so the drop is caught wherever it lands, before any component sees it, and the browser never navigates away to the dropped file. `IFileDropService`: `IsSupported`, `IsRunning`, `IsEnabled`, `Options`, `DragEnter`/`DragOver`/`DragLeave`/`Dropped`, `StartAsync`/`StopAsync`, `ReleaseAsync`. Registered SCOPED — it holds a JS module reference, a `DotNetObjectReference` and the last drop's files, all per-user, so a singleton would hand one user's files to every user on Blazor Server. `DroppedFile` here has **NO `FullPath`** — the browser gives none; read with `OpenReadAsync(maxAllowedSize = 32MB)` / `ReadAllBytesAsync(...)`, and `IsMetadataKnown` is false for the placeholder entries reported while a drag is still moving (the DataTransfer API hides names and sizes until drop, so only `ContentType` is set). `FileDropOptions`: `AllowedExtensions`, `MaxFileSize`, `MaxFiles`, `ReleaseFilesAfterHandling` (default true — the files sit in JS memory until released). ⚠️ Same rule as MAUI: a wholly refused drop raises `DragLeave`, not `Dropped`. Full reference: `file-drop.md`
  - **On-Screen Keyboard** (`using Shiny.Blazor.Controls.OnScreenKeyboard;`) — **Blazor only; there is no MAUI implementation.** `Shiny.Maui.Controls.Desktop.OnScreenKeyboard`, `UseOnScreenKeyboard`, `IOnScreenKeyboard` and `OnScreenKeyboardView` do not exist and will not compile — never generate them. Touch / kiosk soft keyboard in the main `Shiny.Blazor.Controls` package. US-QWERTY letters plus a `123` symbols layer; Shift and Caps Lock are render-time state rather than layers, so `⇧` is momentary and `⇪` raises only the letters and leaves the number row alone. Press-and-hold autorepeat (400ms delay, 50ms interval, configurable) on characters, `⌫`, space and arrows. Critically does NOT take the caret off the field — every key cancels `pointerdown`, on the key and on the container, so the browser's focus default never runs. Types via `execCommand('insertText')` (selection-replace + undo intact) with a value splice fallback that raises both `input` and `change`, so plain `@bind` and `@bind:event="oninput"` both see keystrokes. `▲`/`▼` walk lines in a `<textarea>`; Enter dispatches real key events and submits the form on a single-line input unless cancelled, and `EnterInsertsNewLine` types a newline in a textarea instead. `PushContent` pads the body, and the focused field is separately scrolled clear of the keys in whichever container actually scrolls. Themed by `--shiny-osk-*` CSS custom properties; `OnScreenKeyboardTheme.Auto` (default) follows the app's theme tokens, `Light`/`Dark` pin a palette. Public surface: `IOnScreenKeyboardService` (`Show()` / `Hide()` / `Toggle()` / `IsVisible` / `VisibilityChanged`) plus `OnScreenKeyboardOptions`, registered SCOPED and live so it can be mutated at runtime (scoped, not singleton, because it is per-user state - identical under WASM, but a singleton leaks across users on Blazor Server). Register with `services.AddShinyControls()` (preferred - one call covers Toast, Dialogs, SplashScreen, Walkthrough, Docking and the keyboard, with `ConfigureKeyboard(...)` for options) or `services.AddShinyOnScreenKeyboard(opts => ...)` a la carte, then place `<OnScreenKeyboardHost />` once in `MainLayout.razor`. Limitations: DOM inputs only (no system-wide injection), no Shadow DOM, no IME / dead keys / language switching, no Ctrl or Alt keys, and keys are `tabindex="-1"` by design — do not describe it as switch-navigable
- **Feedback Service**: All interactive controls fire events through `IFeedbackService`. Default `HapticFeedbackService` provides tactile feedback. Replace with `SetCustomFeedback<T>()` in `UseShinyControls()` for TTS, sounds, analytics, or custom responses. The `control` parameter is the actual control instance (use pattern matching like `control is ChatView`), and `args` carries context — `ChatMessage` for ChatView events, native `EventArgs` for standard MAUI controls. Standard MAUI control integration is pluggable and AOT-compatible via `MauiControlFeedbackBuilder` — use `AddDefaultMauiControlFeedback()` for all built-in hooks, add custom hooks with `Hook<TControl>(eventName, subscribe, unsubscribe)`, or use `AddMauiControlFeedback()` for only the hooks you configure

## When to Use This Skill

Invoke this skill when the user wants to:
- Create a settings page or preferences UI in .NET MAUI
- Add or configure TableView cells (switch, checkbox, entry, picker, command, etc.)
- Style a TableView with global cascading styles or per-cell overrides
- Use sections with headers, footers, and dynamic ItemTemplate cells
- Enable drag-to-reorder within a section
- Bind cell properties to a ViewModel using MVVM
- Create radio button groups, date/time pickers, number pickers, or multi-select pickers
- Build any form-like or list-based settings UI
- Build a tree/hierarchical view (file browser, folder picker, org chart, category tree)
- Lazy-load tree branches from a remote source on first expand
- Show a tree with single or multi-select and per-item `CanSelect`/`CanExpand` predicates
- Customize the tree's expand/collapse icons (font icons, custom images, render fragments)
- Enable drag-and-drop reordering within a tree
- Add a bottom sheet / sliding panel / floating panel to a page
- Show status badges, tags, or labels (pill views)
- Display categorized status indicators (success, warning, critical, etc.)
- Overlay a notification badge (count, dot, or label) on the corner of an icon, avatar, button, or card (BadgeView)
- Show an unread count or "99+" overflow indicator on inbox/cart/profile UI elements
- Display a pulsing "NEW"/"!" attention badge on a feature or menu item
- Add a zoomable image viewer / photo viewer overlay
- Display full-screen images with pinch-to-zoom, pan, and double-tap zoom
- Edit images with crop, rotate, draw, or text annotations
- Build an image editor with undo/redo and export
- Build a chat or messaging UI with bubbles, typing indicators, and message history
- Create a conversational interface with load-more pagination and auto-scroll
- Build a PIN entry / OTP / passcode input screen
- Capture numeric or alphanumeric codes in individual cells with optional masking
- Add a floating action button (FAB) to a page, or a speed-dial style multi-action menu
- Expose primary/contextual actions in the bottom corner with animated reveal
- Create scheduler/calendar views (monthly grid, day/week agenda, event list)
- Implement event providers for calendar data
- Customize event templates or day header templates for scheduler views
- Configure agenda timeline with overlap detection, timezone support, and time markers
- Set up infinite scrolling event lists grouped by day
- Build any calendar, appointment, or scheduling UI
- Render markdown text as native MAUI controls
- Build a markdown editor with formatting toolbar and live preview
- Display documentation, notes, or rich text content from markdown strings
- Render a QR code for a URL, vCard, Wi-Fi join code, or pairing token
- Render a 1D barcode (EAN-13, UPC-A, Code 128, etc.) for retail / shipping / IDs
- Render Aztec, Data Matrix, PDF417 codes for transit tickets, electronics labels, or driver's licenses
- Generate barcode PNG bytes, SVG markup, or `data:` URIs from code without a view (e.g., for PDF export, file save, email attachment)
- Pick a QR error-correction level (Low / Medium / Quartile / High) for printed labels or scuff-prone surfaces
- Configure colors, quiet-zone margin, or output size on a barcode / QR code view
- Add a search/autocomplete text input with dropdown suggestions
- Build a typeahead or search-as-you-type control with debounce
- Add a country picker or country selector with flag display
- Build an address search/lookup field with geocoding
- Implement a custom search provider for address or location queries
- Capture a signature or e-signature from the user
- Add a signature pad / drawing pad to a page
- Export a captured signature as a PNG image
- Show toast notifications, snackbar messages, or transient alerts from code
- Show an alert / confirm / prompt dialog from code on MAUI or Blazor (await a result via `IDialogService`), with custom in/out animations
- Build a data grid / data table with sortable, filterable, groupable, editable columns, paging, and selection (MAUI or Blazor) — MudBlazor-style
- Display progress/loading toasts with spinner while operations complete
- Queue or stack multiple notifications
- Replace haptic feedback with custom feedback (text-to-speech, sounds, analytics)
- Wire up IFeedbackService for control interaction events
- Enable text-to-speech on incoming chat messages via feedback service
- Create a text entry field with floating placeholder
- Add validation hints and error states to text inputs
- Build a text input with clear button, character count, or custom tools
- Create a form with styled text entry fields
- Add a gradient slider / temperature slider / range control
- Show a progress bar with gradient fill or shimmer animation
- Build a loading indicator with Vista-style pulse sweep
- Display determinate or indeterminate progress with configurable pulse
- Add a full-screen overlay / loading overlay to a page
- Show a busy/loading indicator over content (spinner or progress bar)
- Create a custom overlay with configurable color and content
- Build a horizontal carousel with snap-to-center or free-scroll behavior
- Create a Netflix-style browsing gallery
- Build a scrollable list with a parallax/hero header (App-Store-style header that translates as you scroll)
- Build a collapsing header that pins to a minimum height once scrolled
- Build a Pinterest-style masonry/waterfall grid with variable-height items
- Create a virtualized grid with grouping, sticky headers, and load-more
- Add load-more pagination (threshold or button) to a collection view
- Add a system tray / status-bar / menu-bar icon to a MAUI desktop app (Windows, macOS, MacCatalyst, Linux)
- Show a right-click context menu with submenus and checkmark items on a tray icon
- Handle left-click / right-click / double-click on a tray icon
- Build a "menu bar app" on macOS or a tray-resident background app on Windows
- Update tray menu items dynamically (e.g. "Pause" ↔ "Resume" labels, enable/disable states)
- Show or hide a tray icon at runtime without recreating it
- Make a tray icon auto-adapt to the macOS light/dark menu bar via template images
- Use Linux AppIndicator (`libayatana-appindicator`) for tray icons on GNOME/KDE
- Build a Visual-Studio-style dockable UI with tool windows, tabbed groups, splitters, auto-hide rails, and tear-off floating windows in a MAUI desktop app
- Add a docking host (`DockHostView` for MAUI / `<DockHost />` for Blazor) to an existing page without subclassing ContentPage
- Register dockable panels by stable string ID (`AddDockPanel<TView>("solution-explorer")`) so layout JSON can resolve them back to actual views
- Make a panel unclosable with `AddDockPanel<TView>("id", canClose: false)` when the surface cannot work without it (a file explorer's folder tree, an editor's document area)
- Persist a dock layout to disk and reload it on app start, with schema versioning and forward migrations via `IDockLayoutMigrator`
- Implement a bring-your-own `IDockLayoutStore` (e.g. backed by Shiny.Stores, a file, or a remote service) to save/load `DockRoot` snapshots
- Observe dock layout/drag/activation events through `IDockEvents` for telemetry or undo-stack integration
- Lock the dock layout for read-only / kiosk modes via `IDockHost.IsLocked = true`
- Accept files dragged from Finder / Explorer / the Files app onto anywhere in a desktop app window
- Handle a file drop that lands **on top of a `BlazorWebView`** or other hosted web content, which `DropGestureRecognizer` cannot see
- Add drag-and-drop file import to a Blazor app over the whole browser window (WASM, Server or Hybrid)
- Stop the browser navigating away when a file is dropped on a Blazor page
- Show a "drop files here" overlay while a drag is over the app, and take it down again when the drop is refused
- Filter dropped files by extension, size or count before the app ever sees them
- Handle dropped files app-wide from a DI service rather than per page (`IFileDropDelegate`)
- Read a dropped file's bytes on Blazor, where the browser gives no file path
- Add a **touch / kiosk on-screen keyboard** to a MAUI desktop or Blazor app
- Show a **soft keyboard** that auto-appears when an `Entry`/`Editor`/`<input>`/`<textarea>` gains focus and types into it without stealing focus
- Build a bottom-docked QWERTY keyboard with shift / numbers / symbols layers
- Drive the on-screen keyboard's visibility from code via `IOnScreenKeyboardService.Show()/Hide()/Toggle()` (Blazor only)
- Ship a kiosk app on a touch tablet without relying on the OS on-screen keyboard
- Build a switch-input-accessible on-screen keyboard (full AutomationPeer / ARIA tree)
- Add an OSK that pushes page content up vs overlays above it (`PushContent` option)
- Distinguish from the OS on-screen keyboard (`osk.exe` / TabTip) — Shiny's OSK is an in-app control, not a wrapper around the OS one

## Library Overview

### .NET MAUI

**NuGet**: `Shiny.Maui.Controls` (+ `Shiny.Maui.Controls.Markdown`, `Shiny.Maui.Controls.MermaidDiagrams`, `Shiny.Maui.Controls.Barcodes`, `Shiny.Maui.Controls.MediaElement` (+ `.Linux` for GTK4), `Shiny.Maui.Controls.Desktop` for tray icon + docking)
**Namespace**: `Shiny.Maui.Controls`
**XAML Namespace**: `http://shiny.net/maui/controls` (prefix: `shiny`)
**Desktop add-on namespaces**: `Shiny.Maui.Controls.Desktop.TrayIcon`, `Shiny.Maui.Controls.Desktop.Docking` (extension methods `UseTrayIcon`, `UseShinyDocking`, `AddDockPanel<T>` live in `Shiny`). There is no `Shiny.Maui.Controls.Desktop.OnScreenKeyboard` — the MAUI on-screen keyboard is not built.

### Blazor

**NuGet**: `Shiny.Blazor.Controls` (+ `Shiny.Blazor.Controls.Markdown`, `Shiny.Blazor.Controls.MermaidDiagrams`, `Shiny.Blazor.Controls.Barcodes`, `Shiny.Blazor.Controls.MediaElement`; the Blazor docking host is in the main package, not an add-on)
**Namespaces**: `Shiny.Blazor.Controls`, `Shiny.Blazor.Controls.Cells`, `Shiny.Blazor.Controls.Sections`, `Shiny.Blazor.Controls.Scheduler`, `Shiny.Blazor.Controls.Chat`, `Shiny.Blazor.Controls.Markdown`, `Shiny.Blazor.Controls.MermaidDiagrams`, `Shiny.Blazor.Controls.Docking`, `Shiny.Blazor.Controls.OnScreenKeyboard`

**Blazor DI registration** — most controls need none: a component brings its own scoped CSS and JS module, so a `<PackageReference>` is enough. Only the service-backed ones need registering, and one call covers all of them, mirroring MAUI's `UseShinyControls()`:

```csharp
using Shiny.Blazor.Controls;

builder.Services.AddShinyControls(cfg => cfg
    .ConfigureDialogs(o => o.DefaultAnimation = DialogAnimation.Zoom)
    .ConfigureKeyboard(o => o.HeightPx = 320)
    .UseHttpImageDownloader()                                  // optional - ShinyImage via HttpClient
    .SetCustomImageDownloader<MyDownloader>()                  // or your own
    .SetCustomDialogs<MyDialogs>()                             // also SetCustomToaster / SetCustomOnScreenKeyboard / SetCustomWalkthroughStore
    .AddDockPanel<ExplorerPanel>("explorer", "Explorer", "📁")
);
```

`AddShinyControls()` registers Toast, Dialogs, SplashScreen, the Walkthrough store, Docking and the On-Screen Keyboard. The à-la-carte calls (`AddShinyToast`, `AddShinyDialogs`, `AddShinySplashScreen`, `AddShinyWalkthrough`, `AddShinyDocking`, `AddShinyOnScreenKeyboard`, `AddShinyImages`) all still exist for apps minimising the WASM payload; every registration is `TryAdd`, so the two styles compose in either order and the FIRST registration wins — register a replacement before `AddShinyControls()`, or use a `SetCustom*` method. Placing a host component (`<ToastHost />`, `<DialogHost />`, `<SplashScreenHost />`, `<DockHost />`, `<OnScreenKeyboardHost />`) without the matching registration throws a DI resolution exception at render time, which is exactly what the umbrella call avoids.

All of these services are registered **Scoped**, never Singleton — they hold per-user UI state. Under WASM the lifetimes are identical, but on Blazor Server a singleton would show one user's toast/dialog/keyboard to every connected user. The single deliberate exception is `DockableContentRegistry` (singleton): an immutable lookup over the registered panel factories with no per-user state.

## Setup

### .NET MAUI

1. Install the NuGet package
   ```bash
   dotnet add package Shiny.Maui.Controls
   ```

2. Configure in `MauiProgram.cs`
   ```csharp
   using Shiny;

   var builder = MauiApp.CreateBuilder();
   builder
       .UseMauiApp<App>()
       .UseShinyControls();
   ```

3. Add the XAML namespace to your pages
   ```xml
   xmlns:shiny="http://shiny.net/maui/controls"
   ```

### Blazor

1. Install the NuGet package
   ```bash
   dotnet add package Shiny.Blazor.Controls
   ```

2. Add `@using` directives (typically in `_Imports.razor`)
   ```razor
   @using Shiny.Blazor.Controls
   @using Shiny.Blazor.Controls.Cells
   @using Shiny.Blazor.Controls.Sections
   @using Shiny.Blazor.Controls.Scheduler
   @using Shiny.Blazor.Controls.Chat
   @using Shiny.Blazor.Controls.Markdown
   @using Shiny.Blazor.Controls.MermaidDiagrams
   ```

No DI registration is required for Blazor — components are used directly in `.razor` pages.

## MAUI → Blazor Translation Cheat Sheet

All controls exist on both hosts, but the Blazor surface is idiomatic Razor, not a 1:1 XAML port. When generating Blazor code, translate with these rules:

### Component name differences

| MAUI (XAML)             | Blazor (Razor)    | Notes                                           |
|-------------------------|-------------------|-------------------------------------------------|
| `shiny:TableView`       | `<TableView>`     | No prefix on Blazor; `TableRoot` is not needed  |
| `shiny:TableRoot`       | *(omitted)*       | Sections go directly inside `<TableView>`       |
| `shiny:TreeView`        | `<TreeView TItem="…">` | Strongly typed on Blazor; `ExpandedIcon`/`CollapsedIcon`/`RetryIcon` are `RenderFragment` slots, not `ImageSource`; Blazor adds keyboard navigation (↑/↓/←/→/Enter/Home/End) and a `<LoadingTemplate>` slot for the root-load spinner |
| `shiny:TableSection`    | `<TableSection>`  |                                                 |
| `shiny:PillView`        | `<Pill>`          | Renamed to just `Pill` on Blazor                |
| `shiny:BadgeView`       | `<BadgeView>`     | Wraps `Content` (MAUI) / `ChildContent` (Blazor). Colors are CSS strings on Blazor; `Position` is the `BadgePosition` enum on both hosts; empty `Text` auto-hides unless `IsDot=true` |
| `shiny:FloatingPanel` in `shiny:OverlayHost` | `<SheetView>` | MAUI uses FloatingPanel+OverlayHost; Blazor uses SheetView with CSS overlay. Content goes in `<SheetContent>` named slot on Blazor |
| `shiny:ShinyButton`     | `<ShinyButton>`   | Same parameters. `LeftIcon`/`RightIcon` are URL-or-SVG strings on Blazor, `ImageSource` on MAUI; `LeftIconView` is `LeftIconContent` (`RenderFragment`); `ContentPadding` is a CSS string. Command state (`CanExecute` → disabled, and AutoBusy while an async command runs) is **MAUI-only** — Blazor awaits `Clicked` instead. `Disabled` on Blazor vs `IsEnabled` on MAUI |
| `shiny:Fab`             | `<Fab>`           | `Icon` takes inline SVG/text string, not `ImageSource` |
| `shiny:FabMenu`         | `<FabMenu>`       | Items passed via `Items` parameter (List<FabMenuItem>), not as children |
| `shiny:ImageViewer`     | `<ImageViewer>`   | `Source` is a URL string on Blazor. The `Uri` loading pipeline (ring, placeholder/error artwork, caching) is **MAUI-only**; Blazor has no thumbnail either — it is overlay-only |
| `shiny:ImageEditor`     | `<ImageEditor>`   | `Source` is `byte[]` (MAUI) or URL string + `ImageData` byte[] (Blazor); colors are CSS strings on Blazor |
| `shiny:ChatView`        | `<ChatView>`      | Colors are CSS strings on Blazor; `SendCommand` is `EventCallback<string>` on Blazor; uses `@using Shiny.Blazor.Controls.Chat` |
| `shiny:SecurityPin`     | `<SecurityPin>`   |                                                 |
| `md:MarkdownView`       | `<MarkdownView>`  |                                                 |
| `md:MarkdownEditor`     | `<MarkdownEditor>`|                                                 |
| `diagram:MermaidDiagramControl` | `<MermaidDiagramControl>` |                                     |
| `shiny:AutoCompleteEntry` | `<AutoCompleteEntry>` | Colors are CSS strings; `SearchCommand` is `EventCallback<string>`; supports `CssClass`, `InputClass`, `DropDownClass`, and `AdditionalAttributes` on Blazor |
| `shiny:CountryPicker`  | `<CountryPicker>` | Colors are CSS strings on Blazor |
| `shiny:AddressEntry`   | `<AddressEntry>`  | Colors are CSS strings on Blazor; uses `IAddressSearchProvider` on both hosts |
| Scheduler views        | `<SchedulerCalendarView>`, `<SchedulerAgendaView>`, `<SchedulerCalendarListView>` | Same names |
| `IToaster.ShowAsync(text, cfg => {})` | `IToastService.ShowAsync(text, cfg => {})` | MAUI uses DI-injected `IToaster` (registered by `UseShinyControls()`); Blazor uses DI-injected `IToastService`. Blazor requires `AddShinyToast()` in DI and `<ToastHost />` in layout |
| `IDialogService.Confirm(...)` (DI) | `IDialogService.Confirm(...)` (DI) | Same interface on both hosts. MAUI auto-attaches via `UseShinyControls()`; Blazor needs `AddShinyDialogs()` + `<DialogHost />`. Colors are `Color` on MAUI, CSS strings on Blazor |
| `shiny:DataGrid` + `shiny:DataGridColumn PropertyName="Name"` (items as `object`) | `<DataGrid TItem="T">` + `<PropertyColumn Property="x => x.Name"/>` (generic; `RenderFragment` cell templates) | MAUI columns nest directly; Blazor columns go in `<Columns>`. Paging: MAUI `PageSize`, Blazor `<PagerContent><DataGridPager/></PagerContent>`. Reorder: MAUI `AllowColumnReorder` (arrows), Blazor `DragDropColumnReordering` (drag) |
| `shiny:TextEntry` | `<TextEntry>` | Colors are CSS strings on Blazor; tools are `List<TextEntryTool>` on both hosts. MAUI uses `ICommand`, Blazor uses `Action` callback. Blazor `TextEntryTool` has `Icon` as string (not ImageSource) |
| `VerticalStackLayout` / `HorizontalStackLayout` (built-in MAUI) | `<VStack>` / `<HStack>` | Blazor only — do **not** invent a MAUI `VStack`. `Spacing` is px on both; alignment is `StackAlign`/`StackJustify` enums on Blazor vs `HorizontalOptions`/`VerticalOptions` on MAUI |
| `Grid` + `OnIdiom` / `VisualStateManager` (built-in MAUI) | `<Grid>` + `<Row>` + `<Column Md="6">` | Blazor only. Responsive spans are viewport media queries, which MAUI has no equivalent of |
| `shiny:DockHostView` (Desktop add-on) or a hand-built `Grid` page | `<AppLayout>` + `<AppLayoutHeader>` / `<AppLayoutPanel>` / `<AppLayoutContent>` / `<AppLayoutFooter>` | Blazor only. Use `AppLayout` for a fixed app shell with collapsible/resizable edge panels; use `DockHost` when the user needs VS-style tabbed, tear-off, user-rearrangeable tool windows |

### Binding, events, content

| MAUI                                                 | Blazor                                           |
|------------------------------------------------------|--------------------------------------------------|
| `IsOpen="{Binding IsOpen, Mode=TwoWay}"`             | `@bind-IsOpen="isOpen"`                          |
| `Value="{Binding Pin, Mode=TwoWay}"`                 | `@bind-Value="pin"`                              |
| `Command="{Binding AddCommand}"`                     | `Clicked="OnAdd"` / `OnClick="OnAdd"` (event callback) |
| `FontAttributes="Bold"` (PillView)                   | `Bold="true"`                                    |
| `Color="DodgerBlue"` (MAUI `Color`)                  | `Color="#1E90FF"` (CSS color strings)            |
| `ItemsSource` + `ItemTemplate` (DataTemplate)        | `ItemsSource` + `ItemTemplate` (`RenderFragment<object>`) |
| `<shiny:FloatingPanel>` content is `[ContentProperty]` PanelContent | `<SheetContent>…</SheetContent>` named slot (Blazor SheetView)    |
| `<shiny:FabMenu><shiny:FabMenuItem /></shiny:FabMenu>` | `Items="List<FabMenuItem>"` parameter         |

### Blazor-specific notes

- Use **CSS color strings** (`"#RRGGBB"`, `"rgb(...)"`, named colors) — there is no MAUI `Color` type on Blazor
- `Icon` on `Fab`/`FabMenuItem` is a string — pass an inline SVG, emoji, or single character
- `RenderFragment<object>` is the Blazor equivalent of `DataTemplate` for `ItemsSource`/`ItemTemplate`
- Event handlers take the event arg directly (e.g. `Completed="OnCompleted"` where `OnCompleted(SecurityPinCompletedEventArgs e)`), not ICommand
- Scheduler still uses `ISchedulerEventProvider` — the same interface and models work on both hosts

### Blazor quick examples

**TableView**
```razor
<TableView CellAccentColor="#10B981">
    <TableSection Title="Profile">
        <LabelCell Title="Name" ValueText="Allan Ritchie" />
        <LabelCell Title="Plan" ValueText="Pro" />
    </TableSection>
    <TableSection Title="Danger zone">
        <ButtonCell Title="Delete account"
                    ButtonTextColor="#DC2626"
                    OnClick="@(() => deleted = true)" />
    </TableSection>
</TableView>
```

**SheetView (Blazor only — MAUI uses FloatingPanel+OverlayHost)**
```razor
<button @onclick="() => isOpen = true">Open Sheet</button>

<SheetView @bind-IsOpen="isOpen"
                 Detents="detents"
                 SheetCornerRadius="20">
    <SheetContent>
        <h2>Hello from a sheet</h2>
        <button @onclick="() => isOpen = false">Close</button>
    </SheetContent>
</SheetView>

@code {
    bool isOpen;
    IList<DetentValue> detents = new List<DetentValue>
    {
        DetentValue.Quarter, DetentValue.Half, DetentValue.Full
    };
}
```

**Pill**
```razor
<Pill Text="Success" Type="PillType.Success" />
<Pill Text="Brand" PillColor="#312E81" PillTextColor="#E0E7FF" />
<Pill Text="Bold" Type="PillType.Info" Bold="true" />
```

**Fab / FabMenu**
```razor
<Fab Icon="+" FabBackgroundColor="#EC4899" Clicked="OnAdd" />

<FabMenu Items="items"
         Icon="+"
         FabBackgroundColor="#7C3AED"
         ItemTapped="OnItemTapped" />

@code {
    readonly List<FabMenuItem> items = new()
    {
        new FabMenuItem { Text = "New Note",  Icon = "📝", FabBackgroundColor = "#10B981", Tag = "note"  },
        new FabMenuItem { Text = "New Photo", Icon = "📷", FabBackgroundColor = "#F59E0B", Tag = "photo" }
    };
    void OnItemTapped(FabMenuItem item) { /* ... */ }
    void OnAdd() { /* ... */ }
}
```

**ImageViewer**
```razor
<img src="@url" @onclick="() => Open(url)" />

<ImageViewer Source="@current" @bind-IsOpen="isOpen" MaxZoom="6" />

@code {
    bool isOpen;
    string? current;
    void Open(string url) { current = url; isOpen = true; }
}
```

**ImageEditor**
```razor
<ImageEditor @ref="editor"
             Source="@imageUrl"
             ImageData="@imageData"
             AllowCrop="true"
             AllowDraw="true"
             AllowRotate="true"
             AllowTextAnnotation="true"
             DrawStrokeColor="#ff0000"
             DrawStrokeWidth="3"
             CanUndoChanged="v => canUndo = v"
             CanRedoChanged="v => canRedo = v" />

@code {
    ImageEditor? editor;
    string? imageUrl = "https://example.com/photo.jpg";
    byte[]? imageData;
    bool canUndo, canRedo;

    async Task Export()
    {
        var bytes = await editor!.ExportAsync("png");
    }
}
```

**ChatView**
```razor
@using Shiny.Blazor.Controls.Chat

<div style="height:600px;">
    <ChatView Messages="messages"
              Participants="participants"
              IsMultiPerson="true"
              TypingParticipants="typingParticipants"
              SendCommand="OnSend"
              AttachImageCommand="OnAttach"
              LoadMoreCommand="OnLoadMore"
              MyBubbleColor="#DCF8C6"
              OtherBubbleColor="#FFFFFF" />
</div>

@code {
    List<ChatMessage> messages = new();
    List<ChatParticipant> participants = new();
    List<ChatParticipant> typingParticipants = new();

    Task OnSend(string text)
    {
        messages.Add(new ChatMessage { Text = text, SenderId = "me", IsFromMe = true });
        StateHasChanged();
        return Task.CompletedTask;
    }

    Task OnAttach() => Task.CompletedTask;
    Task OnLoadMore() => Task.CompletedTask;
}
```

**PasswordStrength**
```razor
<PasswordStrength @ref="field" @bind-Password="passphrase" Placeholder="Passphrase" />
<button disabled="@(field?.IsAcceptable != true)">Create account</button>

@code {
    PasswordStrength? field;
    string passphrase = "";
}
```

**SecurityPin**
```razor
<SecurityPin @bind-Value="pin"
             Length="6"
             HideCharacter="●"
             Completed="OnCompleted" />

@code {
    string pin = "";
    void OnCompleted(SecurityPinCompletedEventArgs e) { /* verify e.Value */ }
}
```

**Markdown**
```razor
<MarkdownView Markdown="@content" />
<MarkdownEditor @bind-Markdown="content" Placeholder="Write markdown…" />
```

**SplashScreen** (Blazor only — see splash-screen.md; the markup goes in index.html, NOT a component)
```html
<!-- wwwroot/index.html — host div OUTSIDE #app, script BEFORE blazor.*.js -->
<link href="_content/Shiny.Blazor.Controls/css/shiny-splash.css" rel="stylesheet" />
...
<div id="app">...</div>
<div id="shiny-splash" data-shiny-splash data-title="My App" data-spinner="ring" data-min-duration="600"></div>
<script src="_content/Shiny.Blazor.Controls/splash.js"></script>
<script src="_framework/blazor.webassembly.js"></script>
```
```csharp
builder.Services.AddShinySplashScreen();   // Shiny.Blazor.Controls.Splash
```
```razor
@* MainLayout.razor / App.razor *@
<SplashScreenHost Until="StartupAsync" />

@code {
    [Inject] ISplashScreen Splash { get; set; } = default!;

    async Task StartupAsync()
    {
        await Splash.SetStatusAsync("Loading accounts…");
        await Splash.SetProgressAsync(0.4);
        await LoadAsync();
    }
}
```

---

# Code Generation Instructions

When generating code with Shiny.Maui.Controls:

### 1. Page Structure
- Always add `xmlns:shiny="http://shiny.net/maui/controls"` to the page
- For Markdown controls: add `xmlns:md="http://shiny.net/maui/markdown"` to the page
- For TableView: wrap content in `shiny:TableView > shiny:TableRoot > shiny:TableSection`
- For FloatingPanel (MAUI): use `shiny:ShinyContentPage` as the page base class with `PageContent` for main content and `Panels` for FloatingPanels. Alternatively, place `shiny:OverlayHost` with `shiny:FloatingPanel` children inside a Grid. Supports `Position="Bottom"` (default), `Position="Top"`, or `Position="BottomTabs"` (for use inside Shell TabBar)
- For SheetView (Blazor only): use `<SheetView>` with `<SheetContent>` child
- For ImageViewer: place `shiny:ImageViewer` inside a Grid that fills the page (it overlays on top, same pattern as SheetView)
- For ImageEditor: use `shiny:ImageEditor` with `Source` bound to `byte[]` image data. Set `AllowX` properties to toggle features. Use `CurrentToolMode` (TwoWay) to control the active tool. Use `CanUndo`/`CanRedo` (OneWayToSource) to observe undo state. Call `ExportAsync()` to save.
- For PillView: use inline within any layout
- For Scheduler views: use `shiny:SchedulerCalendarView`, `shiny:SchedulerAgendaView`, or `shiny:SchedulerCalendarListView` and bind `Provider` to an `ISchedulerEventProvider`
- For MarkdownView: use `md:MarkdownView` anywhere you need to render markdown content
- For MarkdownEditor: use `md:MarkdownEditor` for editable markdown with toolbar and preview

### 2. Cell Selection (TableView)
- Use `SwitchCell` for on/off toggles
- Use `CheckboxCell` for accept/agree checkboxes
- Use `SimpleCheckCell` for selection lists (shows/hides checkmark)
- Use `RadioCell` for mutually exclusive choices within a section
- Use `EntryCell` for text input
- Use `CommandCell` for navigation/action items with disclosure arrow
- Use `ButtonCell` for destructive or primary actions
- Use `LabelCell` for read-only display
- Use `DatePickerCell` / `TimePickerCell` for date/time selection
- Use `TextPickerCell` for dropdown selection from a list
- Use `NumberPickerCell` for numeric input with min/max
- Use `PickerCell` for full-page single or multi-select
- Use `CustomCell` for any custom MAUI view

### 3. Binding Patterns
- Always use `Mode=TwoWay` for editable properties (`On`, `Checked`, `ValueText`, `Date`, `Time`, `Number`, `SelectedIndex`, `SelectedItem`, `SelectedItems`, `IsOpen`, `IsViewerOpen`, `IsPreviewVisible`)
- Use `Mode=TwoWay` for `MarkdownEditor.Markdown` (editor content)
- Use `Mode=OneWay` (default) for display-only properties (`Title`, `Description`, `ValueText` on LabelCell, `Text` on PillView, `Source` on ImageViewer, `Markdown` on MarkdownView)
- Commands use default `Mode=OneWay`
- RadioCell selection binds at section level: `shiny:RadioCell.SelectedValue="{Binding Prop, Mode=TwoWay}"`

### 4. Styling (MAUI)
- Every control accepts an implicit or explicit `Style`. Prefer an app-wide implicit style for
  consistent theming instead of repeating the same properties at each usage site:
  ```xml
  <Style TargetType="shiny:AutoCompleteEntry">
      <Setter Property="FontSize" Value="15" />
      <Setter Property="CornerRadius" Value="12" />
  </Style>
  ```
- Leave colour properties unset to inherit the active Shiny theme; set one explicitly only to
  override that theme default. **A colour you set wins over the theme permanently** — e.g. a
  `ChatView` with `MyBubbleColor="#DCF8C6"` keeps that green through every
  `ShinyThemeManager.SetTheme` call, which looks like theme switching is broken. Omit the property
  unless you actually want to pin it.
- When authoring a **new** control in this repo, never assign a colour literal for chrome
  (`Colors.DodgerBlue`, `Color.FromArgb("#007AFF")`). Use
  `SetDynamicResource(prop, ShinyThemeKeys.Color.X)` so the control repaints live when the theme
  pack changes. `ThemeTokenCoverageTests` fails the build for any file that mentions a colour and
  never references `ShinyThemeKeys`; colours that are genuinely *content* (the ink in SignaturePad,
  the HSV ramp in ColorPicker) are listed there with a reason instead.
  - For a consumer-overridable colour, default the `BindableProperty` to `null` and pick between the
    explicit value and the token at apply time — the pattern used throughout the library:
    ```csharp
    static void Tint(Element target, BindableProperty property, Color? explicitColor, string themeKey)
    {
        if (explicitColor is null)
            target.SetDynamicResource(property, themeKey);
        else
        {
            target.RemoveDynamicResource(property);
            target.SetValue(property, explicitColor);
        }
    }
    ```
- **A theme is not just a palette.** Alongside the colour seeds a pack defines its own **shape**
  (corner geometry), **typography** (family / scale / weight / tracking), **elevation**
  (`shadow` / `flat` / `outline` / `glow`), **density** (spacing ramp and control metrics) and
  **border widths**. That is what makes packs look genuinely unalike — colour alone cannot, because
  the neutral ramp is ΔE ~1 between packs (a tone-98 near-white has no room to carry a hue), so a
  control themed only on `Surface` / `OnSurface` / `Outline` would look identical everywhere.
  Put the accent token on whatever element carries the control's identity, **and** take its radius,
  size, stroke width and shadow from tokens too.
- **Theme packs.** Basic ships in the core packages and applies automatically. The rest install
  separately — `Shiny.{Maui,Blazor}.Controls.Themes.{Ocean,Material,Terminal,Aurora}`. On MAUI select
  one with `cfg.UseOceanTheme()` / `.UseMaterialTheme()` / `.UseTerminalTheme()` / `.UseAuroraTheme()`
  inside `UseShinyControls`; on Blazor `<link>` the pack stylesheet *after* the core
  `shiny-theme.css` so it overrides `:root`.
- **Geometry and type are tokens, same as colour.** Never write a literal for chrome geometry:
  - Blazor `.razor.css` — `border-radius: var(--shiny-shape-corner-medium, 10px)`,
    `font-size: var(--shiny-type-body-medium-size, 14px)` when the value is exactly on the type
    scale, otherwise `calc(13px * var(--shiny-type-scale, 1))` — but never wrap an `em` size that
    way, since `em` already inherits an ancestor the theme scaled; scale once on the control root
    (`font-size: calc(1em * var(--shiny-type-scale, 1))`) and leave the `em` children alone. Same idea for
    `var(--shiny-spacing-N, …)` / `calc(… * var(--shiny-density-scale, 1))` and
    `var(--shiny-border-thin, 1px)`. Leave `border-radius: 50%` alone — a circular avatar or spinner
    is intrinsic geometry.
  - MAUI — an object initializer cannot call `SetDynamicResource`, so chain the helper:
    `new Label { … }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize)`,
    `new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius)`,
    `border.SetDynamicResource(VisualElement.ShadowProperty, ShinyThemeKeys.Elevation.Level3)`.
    `ThemeGeometryCoverageTests` fails the build on a hardcoded on-scale font size, a literal
    `RoundRectangle` radius, or an ad-hoc `new Shadow`.
- **A literal default beats the theme permanently, not just by default.** This is the trap that made
  theme switching look broken: a `BindableProperty` default (MAUI) or a parameter default written
  into an inline `style` (Blazor) is applied to the child at construction, which clears the dynamic
  resource / outranks the stylesheet. Numeric appearance properties therefore default to the
  `ThemeTokens.Unset` sentinel (`-1`) — the same idea as `null` for colours — and resolve through
  `SetTokenOrValue` / `SetCornerTokenOrValue`. On Blazor, emit the declaration only when the
  consumer actually set it, and put the default in the `.razor.css` where the theme can reach it;
  where a parameter is a string, the default can simply *be* the token
  (`CornerRadius = "var(--shiny-shape-corner-small, 8px)"`).
- When authoring a **new** control in this repo, route any `propertyChanged` callback that
  touches a child field through `StyleGuard.WhenReady`, and call
  `StyleGuard.MarkReady(this, typeof(TheControl))` as the last line of the constructor. MAUI
  applies an implicit style from `StyleableElement`'s constructor, before the derived
  constructor body runs, so an unguarded callback dereferences a null child and the page fails
  to inflate. `ImplicitStyleConstructionTests` fails the build if a control misses this.

### 5. FloatingPanel Placement (MAUI)
Use `ShinyContentPage` for the simplest setup:

```xml
<shiny:ShinyContentPage xmlns:shiny="http://shiny.net/maui/controls">
    <shiny:ShinyContentPage.PageContent>
        <ScrollView>
            <VerticalStackLayout>
                <Button Text="Open Panel" Command="{Binding OpenCommand}" />
            </VerticalStackLayout>
        </ScrollView>
    </shiny:ShinyContentPage.PageContent>
    <shiny:ShinyContentPage.Panels>
        <shiny:FloatingPanel IsOpen="{Binding IsOpen, Mode=TwoWay}">
            <Label Text="Panel content" />
        </shiny:FloatingPanel>
    </shiny:ShinyContentPage.Panels>
</shiny:ShinyContentPage>
```

Or use `OverlayHost` manually in a Grid:

```xml
<ContentPage>
    <Grid>
        <ScrollView><!-- page content --></ScrollView>
        <shiny:OverlayHost>
            <shiny:FloatingPanel IsOpen="{Binding IsOpen, Mode=TwoWay}">
                <Label Text="Panel content" />
            </shiny:FloatingPanel>
        </shiny:OverlayHost>
    </Grid>
</ContentPage>
```

### 6. Dark Mode
- Do NOT hardcode colors. Leave color properties unset (`null`) so they resolve from the active
  theme. Every colour default in the library is a theme token, so unset is always the themed answer.
- Only set explicit colors when the design requires specific brand colors.
- **If you do pin a background, pin the matching text colour in the same breath.** The theme's ink
  goes light in dark mode, so a pinned pale fill left with the default text colour ends up
  light-on-light. This is the single most common way generated markup breaks in dark mode.
- The controls respect `Application.Current.UserAppTheme` automatically (MAUI) and the page's
  `color-scheme` (Blazor).
- **Drawn surfaces** — `SpreadsheetView`, `DocumentView`, `DocumentEditor`, `DocumentEditorView`,
  `SlideView`, `SlideEditor`, `SlideEditorView`, `MarkdownView`, `MarkdownEditor` — take a nullable
  `Theme`. **Leave it unset**: unset follows the host scheme live. Only pass
  `SpreadsheetTheme` / `DocumentTheme` / `SlideTheme` / `MarkdownTheme` `.Light` or `.Dark` when the
  surface must stay that way regardless of the app (a preview that has to look like printed paper).
  Never emit `Theme="…Light"` as a default — it pins light and defeats dark mode.
- Blazor: the theme scope class is `shiny-theme-dark` / `shiny-theme-light`, and it can sit on any
  container, not just `<html>`. Colour tokens are `--shiny-color-*` — **not** `--shiny-*`; a wrong
  token name falls back silently to the literal and the control just never follows the theme.

### 7. Styling Strategy
- Set global styles on `shiny:TableView` for consistent appearance
- Override at section level for section-specific header/footer styling
- Override at cell level only for individual cell emphasis
- Use `CellAccentColor` for switches, checkboxes, and radio buttons globally

## Complete TableView Example

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:shiny="http://shiny.net/maui/controls"
             x:Class="MyApp.SettingsPage"
             Title="Settings">

    <shiny:TableView CellSelectedColor="#E0E0E0" CellAccentColor="#007AFF">
        <shiny:TableRoot>
            <shiny:TableSection Title="General">
                <shiny:SwitchCell Title="Notifications" On="{Binding NotificationsOn, Mode=TwoWay}" />
                <shiny:SwitchCell Title="Sound" On="{Binding SoundOn, Mode=TwoWay}" />
                <shiny:CheckboxCell Title="Accept Analytics" Checked="{Binding AnalyticsAccepted, Mode=TwoWay}" />
            </shiny:TableSection>

            <shiny:TableSection Title="Account">
                <shiny:EntryCell Title="Name" ValueText="{Binding Name, Mode=TwoWay}" Placeholder="Your name" />
                <shiny:EntryCell Title="Email" ValueText="{Binding Email, Mode=TwoWay}" Keyboard="Email" />
                <shiny:CommandCell Title="Change Password" Command="{Binding ChangePasswordCommand}" />
            </shiny:TableSection>

            <shiny:TableSection Title="Theme" shiny:RadioCell.SelectedValue="{Binding Theme, Mode=TwoWay}">
                <shiny:RadioCell Title="Light" Value="Light" />
                <shiny:RadioCell Title="Dark" Value="Dark" />
                <shiny:RadioCell Title="System" Value="System" />
            </shiny:TableSection>

            <shiny:TableSection Title="Preferences">
                <shiny:DatePickerCell Title="Birthday" Date="{Binding Birthday, Mode=TwoWay}" Format="D" />
                <shiny:TimePickerCell Title="Daily Reminder" Time="{Binding ReminderTime, Mode=TwoWay}" />
                <shiny:NumberPickerCell Title="Font Size" Number="{Binding FontSize, Mode=TwoWay}"
                                      Min="10" Max="36" Unit="pt" />
            </shiny:TableSection>

            <shiny:TableSection Title="About">
                <shiny:LabelCell Title="Version" ValueText="1.0.0" />
                <shiny:CommandCell Title="Privacy Policy" Command="{Binding PrivacyCommand}" />
                <shiny:CommandCell Title="Terms of Service" Command="{Binding TermsCommand}" />
            </shiny:TableSection>

            <shiny:TableSection Title="Actions">
                <shiny:ButtonCell Title="Sign Out" Command="{Binding SignOutCommand}" ButtonTextColor="Red" />
            </shiny:TableSection>
        </shiny:TableRoot>
    </shiny:TableView>
</ContentPage>
```

## Complete FloatingPanel + PillView Example

```xml
<shiny:ShinyContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:shiny="http://shiny.net/maui/controls"
                         x:Class="MyApp.StatusPage"
                         Title="Status">

    <shiny:ShinyContentPage.PageContent>
        <ScrollView>
            <VerticalStackLayout Padding="20" Spacing="10">
                <Label Text="System Status" FontSize="24" FontAttributes="Bold" />

                <HorizontalStackLayout Spacing="8">
                    <shiny:PillView Text="API" Type="Success" />
                    <shiny:PillView Text="Database" Type="Warning" />
                    <shiny:PillView Text="Queue" Type="Critical" />
                </HorizontalStackLayout>

                <Button Text="View Details" Command="{Binding OpenDetailsCommand}" />
            </VerticalStackLayout>
        </ScrollView>
    </shiny:ShinyContentPage.PageContent>

    <shiny:ShinyContentPage.Panels>
        <shiny:FloatingPanel IsOpen="{Binding IsDetailsOpen, Mode=TwoWay}"
                             PanelCornerRadius="20">
            <VerticalStackLayout Padding="20" Spacing="12">
                <Label Text="Service Details" FontSize="18" FontAttributes="Bold" />

                <HorizontalStackLayout Spacing="6">
                    <shiny:PillView Text="Healthy" Type="Success" />
                    <Label Text="API Server" VerticalOptions="Center" />
                </HorizontalStackLayout>

                <HorizontalStackLayout Spacing="6">
                    <shiny:PillView Text="Degraded" Type="Warning" />
                    <Label Text="Database Cluster" VerticalOptions="Center" />
                </HorizontalStackLayout>

                <HorizontalStackLayout Spacing="6">
                    <shiny:PillView Text="Down" Type="Critical" />
                    <Label Text="Message Queue" VerticalOptions="Center" />
                </HorizontalStackLayout>
            </VerticalStackLayout>
        </shiny:FloatingPanel>
    </shiny:ShinyContentPage.Panels>
</shiny:ShinyContentPage>
```

## Best Practices

1. **Group logically** - Put related settings in the same section with clear headers
2. **Use FooterText** - Explain non-obvious settings in section footers
3. **Two-way bind editable values** - Always `Mode=TwoWay` for user-editable properties
4. **Leave colors null for dark mode** - Only set colors when brand-specific styling is needed
5. **Use CellAccentColor globally** - Set once on TableView instead of per-cell AccentColor
6. **Use CommandCell for navigation** - With `ShowArrow="True"` and `KeepSelectedUntilBack="True"`
7. **Use ButtonCell for destructive actions** - Red text, centered, at the bottom of the page
8. **Use RadioCell for exclusive choices** - Bind `SelectedValue` at the section level
9. **Use PickerCell for long lists** - Full-page picker is better than inline for more than 4-5 items
10. **Use ItemTemplate for dynamic content** - Bind `ItemsSource` on sections for data-driven cells
11. **Use ShinyContentPage or OverlayHost for FloatingPanels** - Use `ShinyContentPage` as the page base class, or place `OverlayHost` in a Grid for overlay panels. Place ImageViewer in a Grid as before
12. **Use PillView for status indicators** - Prefer preset types for consistency; use custom colors for brand-specific needs
13. **Use BadgeView for corner indicators** - Bind `Text` directly to your unread/count source (empty string auto-hides). Use `MaxCount` for numeric overflow ("99+") and `IsDot` for plain "has new" indicators. Reserve `IsPulsing` for genuinely important badges
13. **Use AOT-safe bindings for scheduler templates** - Always use `static (T item) => item.Property` lambda bindings, never string-based
14. **Leave MarkdownView/MarkdownEditor Theme as null** - It auto-resolves Light/Dark based on the app theme
15. **Use MarkdownView for read-only content** - Documentation, notes, changelogs; use MarkdownEditor only when the user needs to edit
16. **ImageViewer Source is set before IsOpen** - Always set the image source before opening the viewer. When neither `Source` nor `Uri` is set, the viewer is automatically InputTransparent so it won't block touches. Use `OpenViewerOnTap="False"` when controlling the viewer programmatically. On MAUI prefer `Uri` for anything remote — it caches, shows a ring, and makes the overlay open instantly
