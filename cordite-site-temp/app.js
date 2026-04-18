// Replace with your Stripe publishable key.
const STRIPE_PUBLISHABLE_KEY = "pk_test_replace_me";
const stripe = Stripe(STRIPE_PUBLISHABLE_KEY);

const TOP_TEN_LOCALES = [
  { code: "en", name: "English" },
  { code: "zh_CN", name: "简体中文" },
  { code: "es", name: "Español" },
  { code: "ar", name: "العربية" },
  { code: "fr", name: "Français" },
  { code: "pt_BR", name: "Português (BR)" },
  { code: "ru", name: "Русский" },
  { code: "de", name: "Deutsch" },
  { code: "ja", name: "日本語" },
  { code: "ko", name: "한국어" }
];

const TRANSLATIONS = {
  en: {
    languageLabel: "Language",
    getDownloads: "Get Downloads",
    heroEyebrow: "Six Fronts. One War.",
    heroTitle: "Command massive RTS battles across land, sea, and sky.",
    heroBody: "Use this section for your key trailer callout and release messaging.",
    featuredVideosTitle: "Featured Videos",
    videoSlot1: "Trailer slot (embed YouTube/Vimeo here)",
    videoSlot2: "Gameplay deep dive slot",
    videoSlot3: "Faction spotlight slot",
    worldScreenshotsTitle: "World & Screenshots",
    worldScreenshotsBody: "Replace these placeholders with faction art, in-game screenshots, and map backgrounds.",
    imageSlot1: "Image slot 1",
    imageSlot2: "Image slot 2",
    imageSlot3: "Image slot 3",
    downloadsTitle: "Downloads (Paywalled)",
    downloadsBody: "Downloads unlock only after successful Stripe payment.",
    paywallLocked: "🔒 Locked — purchase required.",
    paywallUnlocked: "✅ Payment verified. Downloads unlocked for this session.",
    paymentStartError: "Unable to start payment (HTTP {0}). Please try again or contact support.",
    platformWindows: "Windows",
    platformMacOS: "macOS",
    platformLinux: "Linux",
    unlockViaStripe: "Unlock via Stripe",
    downloadWindows: "Download Windows Build",
    downloadMacOS: "Download macOS Build",
    downloadLinux: "Download Linux Build"
  },
  zh_CN: {
    languageLabel: "语言",
    getDownloads: "获取下载",
    heroEyebrow: "六大战线，一场战争。",
    heroTitle: "在陆、海、空战场上指挥大规模 RTS 战斗。",
    heroBody: "在此展示你的核心预告片信息与发售信息。",
    featuredVideosTitle: "精选视频",
    videoSlot1: "预告片位置（在此嵌入 YouTube/Vimeo）",
    videoSlot2: "玩法深度解析位置",
    videoSlot3: "阵营聚焦位置",
    worldScreenshotsTitle: "世界与截图",
    worldScreenshotsBody: "用阵营美术、游戏截图和地图背景替换这些占位内容。",
    imageSlot1: "图片位置 1",
    imageSlot2: "图片位置 2",
    imageSlot3: "图片位置 3",
    downloadsTitle: "下载（付费墙）",
    downloadsBody: "仅在 Stripe 支付成功后解锁下载。",
    paywallLocked: "🔒 已锁定 — 需要购买。",
    paywallUnlocked: "✅ 支付验证成功。本次会话下载已解锁。",
    paymentStartError: "无法启动支付（HTTP {0}）。请重试或联系支持。",
    platformWindows: "Windows",
    platformMacOS: "macOS",
    platformLinux: "Linux",
    unlockViaStripe: "通过 Stripe 解锁",
    downloadWindows: "下载 Windows 版本",
    downloadMacOS: "下载 macOS 版本",
    downloadLinux: "下载 Linux 版本"
  },
  es: {
    languageLabel: "Idioma",
    getDownloads: "Obtener descargas",
    heroEyebrow: "Seis frentes. Una guerra.",
    heroTitle: "Comanda batallas RTS masivas por tierra, mar y aire.",
    heroBody: "Usa esta sección para tu llamado del tráiler y mensaje de lanzamiento.",
    featuredVideosTitle: "Videos destacados",
    videoSlot1: "Espacio para tráiler (inserta YouTube/Vimeo aquí)",
    videoSlot2: "Espacio para análisis de jugabilidad",
    videoSlot3: "Espacio para enfoque de facción",
    worldScreenshotsTitle: "Mundo y capturas",
    worldScreenshotsBody: "Reemplaza estos espacios con arte de facciones, capturas y fondos de mapas.",
    imageSlot1: "Espacio de imagen 1",
    imageSlot2: "Espacio de imagen 2",
    imageSlot3: "Espacio de imagen 3",
    downloadsTitle: "Descargas (con pago)",
    downloadsBody: "Las descargas se desbloquean solo tras un pago exitoso en Stripe.",
    paywallLocked: "🔒 Bloqueado — se requiere compra.",
    paywallUnlocked: "✅ Pago verificado. Descargas desbloqueadas para esta sesión.",
    paymentStartError: "No se pudo iniciar el pago (HTTP {0}). Intenta de nuevo o contacta soporte.",
    platformWindows: "Windows",
    platformMacOS: "macOS",
    platformLinux: "Linux",
    unlockViaStripe: "Desbloquear con Stripe",
    downloadWindows: "Descargar versión de Windows",
    downloadMacOS: "Descargar versión de macOS",
    downloadLinux: "Descargar versión de Linux"
  },
  ar: {
    languageLabel: "اللغة",
    getDownloads: "الحصول على التنزيلات",
    heroEyebrow: "ست جبهات. حرب واحدة.",
    heroTitle: "قد معارك RTS ضخمة عبر البر والبحر والجو.",
    heroBody: "استخدم هذا القسم لرسالة العرض الترويجي والإطلاق.",
    featuredVideosTitle: "فيديوهات مميزة",
    videoSlot1: "مكان المقطع الدعائي (أدرج YouTube/Vimeo هنا)",
    videoSlot2: "مكان التعمق في أسلوب اللعب",
    videoSlot3: "مكان تسليط الضوء على الفصيل",
    worldScreenshotsTitle: "العالم ولقطات الشاشة",
    worldScreenshotsBody: "استبدل هذه العناصر الفنية بصور الفصائل ولقطات اللعب وخلفيات الخرائط.",
    imageSlot1: "مكان الصورة 1",
    imageSlot2: "مكان الصورة 2",
    imageSlot3: "مكان الصورة 3",
    downloadsTitle: "التنزيلات (خلف جدار الدفع)",
    downloadsBody: "يتم فتح التنزيلات فقط بعد نجاح الدفع عبر Stripe.",
    paywallLocked: "🔒 مقفل — الشراء مطلوب.",
    paywallUnlocked: "✅ تم التحقق من الدفع. تم فتح التنزيلات لهذه الجلسة.",
    paymentStartError: "تعذر بدء الدفع (HTTP {0}). حاول مرة أخرى أو تواصل مع الدعم.",
    platformWindows: "Windows",
    platformMacOS: "macOS",
    platformLinux: "Linux",
    unlockViaStripe: "فتح عبر Stripe",
    downloadWindows: "تنزيل إصدار Windows",
    downloadMacOS: "تنزيل إصدار macOS",
    downloadLinux: "تنزيل إصدار Linux"
  },
  fr: {
    languageLabel: "Langue",
    getDownloads: "Téléchargements",
    heroEyebrow: "Six fronts. Une guerre.",
    heroTitle: "Commandez d'immenses batailles RTS sur terre, mer et ciel.",
    heroBody: "Utilisez cette section pour votre message de bande-annonce et de sortie.",
    featuredVideosTitle: "Vidéos à la une",
    videoSlot1: "Emplacement bande-annonce (intégrer YouTube/Vimeo ici)",
    videoSlot2: "Emplacement analyse gameplay",
    videoSlot3: "Emplacement focus faction",
    worldScreenshotsTitle: "Univers et captures",
    worldScreenshotsBody: "Remplacez ces espaces par des visuels de factions, captures en jeu et fonds de carte.",
    imageSlot1: "Emplacement image 1",
    imageSlot2: "Emplacement image 2",
    imageSlot3: "Emplacement image 3",
    downloadsTitle: "Téléchargements (payants)",
    downloadsBody: "Les téléchargements se débloquent uniquement après paiement Stripe réussi.",
    paywallLocked: "🔒 Verrouillé — achat requis.",
    paywallUnlocked: "✅ Paiement vérifié. Téléchargements débloqués pour cette session.",
    paymentStartError: "Impossible de démarrer le paiement (HTTP {0}). Réessayez ou contactez le support.",
    platformWindows: "Windows",
    platformMacOS: "macOS",
    platformLinux: "Linux",
    unlockViaStripe: "Débloquer via Stripe",
    downloadWindows: "Télécharger la version Windows",
    downloadMacOS: "Télécharger la version macOS",
    downloadLinux: "Télécharger la version Linux"
  },
  pt_BR: {
    languageLabel: "Idioma",
    getDownloads: "Obter downloads",
    heroEyebrow: "Seis frentes. Uma guerra.",
    heroTitle: "Comande batalhas RTS massivas por terra, mar e ar.",
    heroBody: "Use esta seção para seu destaque de trailer e mensagem de lançamento.",
    featuredVideosTitle: "Vídeos em destaque",
    videoSlot1: "Espaço do trailer (incorpore YouTube/Vimeo aqui)",
    videoSlot2: "Espaço para análise de gameplay",
    videoSlot3: "Espaço para destaque de facção",
    worldScreenshotsTitle: "Mundo e capturas",
    worldScreenshotsBody: "Substitua estes espaços por artes de facções, capturas de jogo e fundos de mapa.",
    imageSlot1: "Espaço de imagem 1",
    imageSlot2: "Espaço de imagem 2",
    imageSlot3: "Espaço de imagem 3",
    downloadsTitle: "Downloads (com paywall)",
    downloadsBody: "Os downloads só são liberados após pagamento Stripe aprovado.",
    paywallLocked: "🔒 Bloqueado — compra necessária.",
    paywallUnlocked: "✅ Pagamento verificado. Downloads liberados para esta sessão.",
    paymentStartError: "Não foi possível iniciar o pagamento (HTTP {0}). Tente novamente ou contate o suporte.",
    platformWindows: "Windows",
    platformMacOS: "macOS",
    platformLinux: "Linux",
    unlockViaStripe: "Desbloquear via Stripe",
    downloadWindows: "Baixar versão Windows",
    downloadMacOS: "Baixar versão macOS",
    downloadLinux: "Baixar versão Linux"
  },
  ru: {
    languageLabel: "Язык",
    getDownloads: "Скачать",
    heroEyebrow: "Шесть фронтов. Одна война.",
    heroTitle: "Командуйте масштабными RTS-битвами на суше, море и в небе.",
    heroBody: "Используйте этот блок для трейлера и ключевого релизного сообщения.",
    featuredVideosTitle: "Рекомендуемые видео",
    videoSlot1: "Слот трейлера (вставьте YouTube/Vimeo здесь)",
    videoSlot2: "Слот разбора геймплея",
    videoSlot3: "Слот обзора фракции",
    worldScreenshotsTitle: "Мир и скриншоты",
    worldScreenshotsBody: "Замените эти плейсхолдеры артом фракций, скриншотами и фонами карт.",
    imageSlot1: "Слот изображения 1",
    imageSlot2: "Слот изображения 2",
    imageSlot3: "Слот изображения 3",
    downloadsTitle: "Загрузки (платный доступ)",
    downloadsBody: "Загрузки открываются только после успешной оплаты через Stripe.",
    paywallLocked: "🔒 Закрыто — требуется покупка.",
    paywallUnlocked: "✅ Оплата подтверждена. Загрузки открыты для этой сессии.",
    paymentStartError: "Не удалось начать оплату (HTTP {0}). Повторите попытку или обратитесь в поддержку.",
    platformWindows: "Windows",
    platformMacOS: "macOS",
    platformLinux: "Linux",
    unlockViaStripe: "Разблокировать через Stripe",
    downloadWindows: "Скачать сборку для Windows",
    downloadMacOS: "Скачать сборку для macOS",
    downloadLinux: "Скачать сборку для Linux"
  },
  de: {
    languageLabel: "Sprache",
    getDownloads: "Downloads holen",
    heroEyebrow: "Sechs Fronten. Ein Krieg.",
    heroTitle: "Führe gewaltige RTS-Schlachten zu Land, zu Wasser und in der Luft.",
    heroBody: "Nutze diesen Bereich für Trailer-Highlights und Release-Botschaften.",
    featuredVideosTitle: "Empfohlene Videos",
    videoSlot1: "Trailer-Slot (YouTube/Vimeo hier einbetten)",
    videoSlot2: "Gameplay-Deep-Dive-Slot",
    videoSlot3: "Fraktions-Spotlight-Slot",
    worldScreenshotsTitle: "Welt & Screenshots",
    worldScreenshotsBody: "Ersetze diese Platzhalter mit Fraktions-Artwork, Ingame-Screenshots und Kartenhintergründen.",
    imageSlot1: "Bild-Slot 1",
    imageSlot2: "Bild-Slot 2",
    imageSlot3: "Bild-Slot 3",
    downloadsTitle: "Downloads (Paywall)",
    downloadsBody: "Downloads werden erst nach erfolgreicher Stripe-Zahlung freigeschaltet.",
    paywallLocked: "🔒 Gesperrt — Kauf erforderlich.",
    paywallUnlocked: "✅ Zahlung bestätigt. Downloads für diese Sitzung freigeschaltet.",
    paymentStartError: "Zahlung konnte nicht gestartet werden (HTTP {0}). Bitte erneut versuchen oder Support kontaktieren.",
    platformWindows: "Windows",
    platformMacOS: "macOS",
    platformLinux: "Linux",
    unlockViaStripe: "Über Stripe freischalten",
    downloadWindows: "Windows-Build herunterladen",
    downloadMacOS: "macOS-Build herunterladen",
    downloadLinux: "Linux-Build herunterladen"
  },
  ja: {
    languageLabel: "言語",
    getDownloads: "ダウンロード",
    heroEyebrow: "六つの戦線。ひとつの戦争。",
    heroTitle: "陸・海・空をまたぐ大規模RTSバトルを指揮せよ。",
    heroBody: "このセクションはトレーラー訴求やリリース告知に使えます。",
    featuredVideosTitle: "注目動画",
    videoSlot1: "トレーラー枠（YouTube/Vimeoを埋め込み）",
    videoSlot2: "ゲームプレイ解説枠",
    videoSlot3: "勢力紹介枠",
    worldScreenshotsTitle: "世界観＆スクリーンショット",
    worldScreenshotsBody: "このプレースホルダーを勢力アート、ゲーム画面、マップ背景に置き換えてください。",
    imageSlot1: "画像枠 1",
    imageSlot2: "画像枠 2",
    imageSlot3: "画像枠 3",
    downloadsTitle: "ダウンロード（有料）",
    downloadsBody: "ダウンロードはStripe決済成功後にのみ解放されます。",
    paywallLocked: "🔒 ロック中 — 購入が必要です。",
    paywallUnlocked: "✅ 支払いを確認しました。このセッションのダウンロードが解放されました。",
    paymentStartError: "決済を開始できませんでした（HTTP {0}）。再試行するかサポートに連絡してください。",
    platformWindows: "Windows",
    platformMacOS: "macOS",
    platformLinux: "Linux",
    unlockViaStripe: "Stripeで解放",
    downloadWindows: "Windows版をダウンロード",
    downloadMacOS: "macOS版をダウンロード",
    downloadLinux: "Linux版をダウンロード"
  },
  ko: {
    languageLabel: "언어",
    getDownloads: "다운로드 받기",
    heroEyebrow: "여섯 전선, 하나의 전쟁.",
    heroTitle: "육해공을 가르는 대규모 RTS 전투를 지휘하세요.",
    heroBody: "이 섹션에 트레일러 핵심 메시지와 출시 안내를 넣으세요.",
    featuredVideosTitle: "추천 영상",
    videoSlot1: "트레일러 슬롯 (YouTube/Vimeo 임베드)",
    videoSlot2: "게임플레이 심층 소개 슬롯",
    videoSlot3: "진영 소개 슬롯",
    worldScreenshotsTitle: "세계관 및 스크린샷",
    worldScreenshotsBody: "이 자리표시자를 진영 아트, 인게임 스크린샷, 맵 배경으로 교체하세요.",
    imageSlot1: "이미지 슬롯 1",
    imageSlot2: "이미지 슬롯 2",
    imageSlot3: "이미지 슬롯 3",
    downloadsTitle: "다운로드 (유료 잠금)",
    downloadsBody: "Stripe 결제가 성공한 후에만 다운로드가 열립니다.",
    paywallLocked: "🔒 잠김 — 구매가 필요합니다.",
    paywallUnlocked: "✅ 결제 확인 완료. 이 세션의 다운로드가 열렸습니다.",
    paymentStartError: "결제를 시작할 수 없습니다 (HTTP {0}). 다시 시도하거나 지원팀에 문의하세요.",
    platformWindows: "Windows",
    platformMacOS: "macOS",
    platformLinux: "Linux",
    unlockViaStripe: "Stripe로 잠금 해제",
    downloadWindows: "Windows 빌드 다운로드",
    downloadMacOS: "macOS 빌드 다운로드",
    downloadLinux: "Linux 빌드 다운로드"
  }
};

const DEFAULT_LOCALE = "en";
const LANGUAGE_STORAGE_KEY = "cordite.language";

const paywallMessage = document.getElementById("paywall-message");
const buttons = document.querySelectorAll("button[data-product]");
const downloadLinks = document.querySelectorAll("a[data-download]");
const languageSelect = document.getElementById("language-select");
let currentLocale = DEFAULT_LOCALE;

for (const button of buttons) {
  button.addEventListener("click", async () => {
    const product = button.dataset.product;
    await startCheckout(product);
  });
}

initializeLanguageSupport();

async function startCheckout(product) {
  // Backend endpoint expected:
  // POST /api/create-checkout-session with JSON { product }
  // Response: { id: "cs_test_..." }
  const response = await fetch("/api/create-checkout-session", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ product })
  });

  if (!response.ok) {
    paywallMessage.textContent = translate("paymentStartError").replace("{0}", String(response.status));
    return;
  }

  const session = await response.json();
  await stripe.redirectToCheckout({ sessionId: session.id });
}

async function tryUnlockFromSession() {
  const params = new URLSearchParams(window.location.search);
  const sessionId = params.get("session_id");
  if (!sessionId) return;
  if (!/^cs_(test_|live_)?[A-Za-z0-9_]+$/.test(sessionId)) return;

  // Backend endpoint expected:
  // GET /api/download-entitlements?session_id=...
  // Response: { paid: true, downloadUrls: { windows, macos, linux } }
  const response = await fetch(`/api/download-entitlements?session_id=${encodeURIComponent(sessionId)}`);
  if (!response.ok) return;

  const result = await response.json();
  if (!result.paid) return;

  for (const link of downloadLinks) {
    const key = link.dataset.download;
    const href = result.downloadUrls?.[key];
    if (href) {
      link.href = href;
      link.classList.add("unlocked");
      link.removeAttribute("aria-disabled");
    }
  }
  paywallMessage.textContent = translate("paywallUnlocked");
}

function initializeLanguageSupport() {
  for (const locale of TOP_TEN_LOCALES) {
    const option = document.createElement("option");
    option.value = locale.code;
    option.textContent = locale.name;
    languageSelect.appendChild(option);
  }

  const browserLocale = navigator.language?.replace("-", "_");
  const browserBase = browserLocale?.split("_")[0];
  const preferred = localStorage.getItem(LANGUAGE_STORAGE_KEY);
  const matchedBrowser =
    TOP_TEN_LOCALES.find((item) => item.code === browserLocale)?.code ??
    TOP_TEN_LOCALES.find((item) => item.code.startsWith(`${browserBase}_`) || item.code === browserBase)?.code;

  setLocale(preferred && isSupportedLocale(preferred) ? preferred : matchedBrowser ?? DEFAULT_LOCALE);

  languageSelect.addEventListener("change", () => {
    setLocale(languageSelect.value);
  });
}

function setLocale(localeCode) {
  currentLocale = isSupportedLocale(localeCode) ? localeCode : DEFAULT_LOCALE;
  languageSelect.value = currentLocale;
  localStorage.setItem(LANGUAGE_STORAGE_KEY, currentLocale);
  applyTranslations();
}

function isSupportedLocale(localeCode) {
  return TOP_TEN_LOCALES.some((locale) => locale.code === localeCode);
}

function applyTranslations() {
  const isArabic = currentLocale === "ar";
  document.documentElement.lang = currentLocale === "zh_CN" ? "zh-CN" : currentLocale.replace("_", "-");
  document.documentElement.dir = isArabic ? "rtl" : "ltr";

  const elements = document.querySelectorAll("[data-i18n]");
  for (const element of elements) {
    const key = element.dataset.i18n;
    if (!key) continue;
    element.textContent = translate(key);
  }
}

function translate(key) {
  return TRANSLATIONS[currentLocale]?.[key] ?? TRANSLATIONS[DEFAULT_LOCALE][key] ?? key;
}

tryUnlockFromSession();
