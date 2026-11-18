/**
 * High-fidelity SVG vector images representing plywood product views,
 * cross-sections, architectural applications, and specialized boards.
 * Rendered as data URIs for instantaneous, zero-latency, 100% reliable local preview.
 */

function encodeSvg(svg: string): string {
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg.trim())}`;
}

/**
 * 1. Plywood Master Sheet: Calibrated natural hardwood face veneer.
 */
export const PLYWOOD_FACE_SHEET_SVG = encodeSvg(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300" width="400" height="300">
  <defs>
    <linearGradient id="sheetBg" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#dfb98a"/>
      <stop offset="35%" stop-color="#d4aa76"/>
      <stop offset="70%" stop-color="#cb9d64"/>
      <stop offset="100%" stop-color="#be8f53"/>
    </linearGradient>
    <filter id="woodGrain" x="0%" y="0%" width="100%" height="100%">
      <feTurbulence type="fractalNoise" baseFrequency="0.035 0.003" numOctaves="4" result="noise"/>
      <feColorMatrix type="matrix" values="
        1 0 0 0 0
        0 0.85 0 0 0
        0 0 0.65 0 0
        0 0 0 0.42 0" result="coloredNoise"/>
      <feComposite in="SourceGraphic" in2="coloredNoise" operator="arithmetic" k1="0" k2="0.82" k3="0.25" k4="0"/>
    </filter>
    <linearGradient id="sheen" x1="0%" y1="0%" x2="60%" y2="100%">
      <stop offset="0%" stop-color="#ffffff" stop-opacity="0.22"/>
      <stop offset="50%" stop-color="#ffffff" stop-opacity="0.04"/>
      <stop offset="100%" stop-color="#000000" stop-opacity="0.18"/>
    </linearGradient>
  </defs>
  <!-- Base calibrated sheet -->
  <rect width="400" height="300" fill="url(#sheetBg)"/>
  <rect width="400" height="300" fill="url(#sheetBg)" filter="url(#woodGrain)" opacity="0.95"/>
  
  <!-- Subtle natural veneer flow lines -->
  <path d="M-20 40 Q 150 70, 420 30" stroke="#a2723c" stroke-width="1.2" fill="none" opacity="0.35"/>
  <path d="M-20 85 Q 220 110, 420 75" stroke="#936430" stroke-width="1.8" fill="none" opacity="0.3"/>
  <path d="M-20 145 Q 180 180, 420 130" stroke="#b0814a" stroke-width="1.4" fill="none" opacity="0.32"/>
  <path d="M-20 195 Q 260 220, 420 185" stroke="#9b6c36" stroke-width="1.6" fill="none" opacity="0.3"/>
  <path d="M-20 255 Q 160 275, 420 240" stroke="#8c5d29" stroke-width="2.0" fill="none" opacity="0.28"/>
  
  <!-- Subtle organic veneer cathedrals -->
  <path d="M 120 310 C 130 190, 240 180, 270 310" stroke="#8a5b28" stroke-width="2.2" fill="none" opacity="0.25"/>
  <path d="M 140 310 C 150 215, 220 210, 250 310" stroke="#9a6933" stroke-width="1.6" fill="none" opacity="0.22"/>
  
  <!-- Surface lighting sheen & edge bevel -->
  <rect width="400" height="300" fill="url(#sheen)"/>
  <rect x="1" y="1" width="398" height="298" fill="none" stroke="#f6deb7" stroke-width="1.2" opacity="0.6"/>
  <rect x="0" y="0" width="400" height="300" fill="none" stroke="#794e1d" stroke-width="1.5" opacity="0.8"/>
  
  <!-- Calibrated thickness label badge -->
  <g transform="translate(14, 256)">
    <rect width="116" height="28" rx="4" fill="#1e293b" opacity="0.82"/>
    <text x="12" y="18" fill="#f8fafc" font-family="Inter, system-ui, sans-serif" font-size="11" font-weight="600" letter-spacing="0.5">19mm CALIBRATED</text>
  </g>
</svg>
`);

/**
 * 2. Multi-Ply Edge Cross Section: Precision 13-ply alternating hardwood construction.
 */
export const PLYWOOD_CROSS_SECTION_SVG = encodeSvg(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300" width="400" height="300">
  <defs>
    <linearGradient id="bg" x1="0%" y1="0%" x2="0%" y2="100%">
      <stop offset="0%" stop-color="#1e2430"/>
      <stop offset="100%" stop-color="#0f131a"/>
    </linearGradient>
    <linearGradient id="plyLight" x1="0%" y1="0%" x2="100%" y2="0%">
      <stop offset="0%" stop-color="#eed5b2"/>
      <stop offset="50%" stop-color="#f5e2c7"/>
      <stop offset="100%" stop-color="#e3c79e"/>
    </linearGradient>
    <linearGradient id="plyDark" x1="0%" y1="0%" x2="100%" y2="0%">
      <stop offset="0%" stop-color="#b67a48"/>
      <stop offset="50%" stop-color="#ca8a54"/>
      <stop offset="100%" stop-color="#9e6435"/>
    </linearGradient>
    <linearGradient id="resinLine" x1="0%" y1="0%" x2="100%" y2="0%">
      <stop offset="0%" stop-color="#3b1d11"/>
      <stop offset="100%" stop-color="#2a1208"/>
    </linearGradient>
  </defs>
  
  <rect width="400" height="300" fill="url(#bg)"/>
  
  <!-- Structural Board Section -->
  <g transform="translate(30, 45)">
    <!-- Shadow -->
    <rect x="0" y="210" width="340" height="15" fill="#000" opacity="0.4" filter="blur(6px)"/>
    
    <!-- Top Face Veneer (Light birch/gurjan) -->
    <rect x="0" y="0" width="340" height="16" fill="url(#plyLight)"/>
    <rect x="0" y="16" width="340" height="2" fill="url(#resinLine)"/>
    
    <!-- Ply 2: Cross grain core -->
    <rect x="0" y="18" width="340" height="15" fill="url(#plyDark)"/>
    <rect x="0" y="33" width="340" height="2" fill="url(#resinLine)"/>
    
    <!-- Ply 3 -->
    <rect x="0" y="35" width="340" height="15" fill="url(#plyLight)"/>
    <rect x="0" y="50" width="340" height="2" fill="url(#resinLine)"/>
    
    <!-- Ply 4 -->
    <rect x="0" y="52" width="340" height="15" fill="url(#plyDark)"/>
    <rect x="0" y="67" width="340" height="2" fill="url(#resinLine)"/>
    
    <!-- Ply 5: Center core hardwood -->
    <rect x="0" y="69" width="340" height="20" fill="url(#plyLight)"/>
    <rect x="0" y="89" width="340" height="2" fill="url(#resinLine)"/>
    
    <!-- Ply 6 -->
    <rect x="0" y="91" width="340" height="15" fill="url(#plyDark)"/>
    <rect x="0" y="106" width="340" height="2" fill="url(#resinLine)"/>
    
    <!-- Ply 7 -->
    <rect x="0" y="108" width="340" height="15" fill="url(#plyLight)"/>
    <rect x="0" y="123" width="340" height="2" fill="url(#resinLine)"/>
    
    <!-- Ply 8 -->
    <rect x="0" y="125" width="340" height="15" fill="url(#plyDark)"/>
    <rect x="0" y="140" width="340" height="2" fill="url(#resinLine)"/>
    
    <!-- Ply 9: Bottom Face -->
    <rect x="0" y="142" width="340" height="16" fill="url(#plyLight)"/>
    
    <!-- End grain grain striping texture -->
    <g stroke="#713f1c" stroke-width="0.8" opacity="0.35">
      <line x1="25" y1="18" x2="25" y2="33"/>
      <line x1="65" y1="18" x2="65" y2="33"/>
      <line x1="110" y1="18" x2="110" y2="33"/>
      <line x1="160" y1="18" x2="160" y2="33"/>
      <line x1="215" y1="18" x2="215" y2="33"/>
      <line x1="270" y1="18" x2="270" y2="33"/>
      <line x1="315" y1="18" x2="315" y2="33"/>

      <line x1="40" y1="52" x2="40" y2="67"/>
      <line x1="90" y1="52" x2="90" y2="67"/>
      <line x1="145" y1="52" x2="145" y2="67"/>
      <line x1="195" y1="52" x2="195" y2="67"/>
      <line x1="250" y1="52" x2="250" y2="67"/>
      <line x1="295" y1="52" x2="295" y2="67"/>

      <line x1="30" y1="91" x2="30" y2="106"/>
      <line x1="75" y1="91" x2="75" y2="106"/>
      <line x1="130" y1="91" x2="130" y2="106"/>
      <line x1="180" y1="91" x2="180" y2="106"/>
      <line x1="235" y1="91" x2="235" y2="106"/>
      <line x1="285" y1="91" x2="285" y2="106"/>
    </g>

    <!-- Dimension indicator overlay -->
    <line x1="-12" y1="0" x2="-12" y2="158" stroke="#38bdf8" stroke-width="1.5"/>
    <line x1="-18" y1="0" x2="-6" y2="0" stroke="#38bdf8" stroke-width="1.5"/>
    <line x1="-18" y1="158" x2="-6" y2="158" stroke="#38bdf8" stroke-width="1.5"/>
    <text x="-24" y="84" fill="#38bdf8" font-family="Inter, sans-serif" font-size="10" font-weight="700" text-anchor="middle" transform="rotate(-90, -24, 84)">19 mm ±0.2</text>
  </g>

  <!-- Technical label -->
  <g transform="translate(14, 256)">
    <rect width="170" height="28" rx="4" fill="#0f172a" opacity="0.85"/>
    <text x="12" y="18" fill="#38bdf8" font-family="Inter, sans-serif" font-size="10.5" font-weight="600">PENTA-TECH 9-PLY CORE</text>
  </g>
</svg>
`);

/**
 * 3. Modern Architectural Kitchen Joinery / Application.
 */
export const PLYWOOD_APPLICATION_KITCHEN_SVG = encodeSvg(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300" width="400" height="300">
  <defs>
    <linearGradient id="wallBg" x1="0%" y1="0%" x2="0%" y2="100%">
      <stop offset="0%" stop-color="#e2e8f0"/>
      <stop offset="100%" stop-color="#cbd5e1"/>
    </linearGradient>
    <linearGradient id="plyCarcass" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#e9cfac"/>
      <stop offset="100%" stop-color="#cca87d"/>
    </linearGradient>
    <linearGradient id="countertop" x1="0%" y1="0%" x2="100%" y2="0%">
      <stop offset="0%" stop-color="#334155"/>
      <stop offset="100%" stop-color="#1e293b"/>
    </linearGradient>
  </defs>

  <!-- Room background -->
  <rect width="400" height="300" fill="url(#wallBg)"/>
  <rect x="0" y="240" width="400" height="60" fill="#94a3b8" opacity="0.6"/>

  <!-- Upper Plywood Cabinets with Exposed Edge Detail -->
  <g transform="translate(40, 25)">
    <!-- Cabinet Box 1 -->
    <rect x="0" y="0" width="100" height="90" fill="url(#plyCarcass)" stroke="#9c784f" stroke-width="1.5"/>
    <line x1="8" y1="8" x2="92" y2="8" stroke="#845e35" stroke-width="2"/>
    <line x1="8" y1="45" x2="92" y2="45" stroke="#845e35" stroke-width="2"/>
    <circle cx="50" cy="72" r="3" fill="#1e293b"/>

    <!-- Cabinet Box 2 -->
    <rect x="110" y="0" width="100" height="90" fill="url(#plyCarcass)" stroke="#9c784f" stroke-width="1.5"/>
    <line x1="118" y1="8" x2="202" y2="8" stroke="#845e35" stroke-width="2"/>
    <line x1="118" y1="45" x2="202" y2="45" stroke="#845e35" stroke-width="2"/>
    <circle cx="160" cy="72" r="3" fill="#1e293b"/>

    <!-- Open Plywood Shelving 3 -->
    <rect x="220" y="0" width="100" height="90" fill="#edd6b8" stroke="#9c784f" stroke-width="1.5"/>
    <rect x="224" y="28" width="92" height="6" fill="#cca87d" stroke="#845e35" stroke-width="1"/>
    <rect x="224" y="58" width="92" height="6" fill="#cca87d" stroke="#845e35" stroke-width="1"/>
    <!-- Ceramic decor props -->
    <ellipse cx="245" cy="22" rx="8" ry="6" fill="#ffffff" opacity="0.9"/>
    <rect x="270" y="14" width="12" height="14" rx="2" fill="#38bdf8" opacity="0.8"/>
  </g>

  <!-- Solid Quartz Countertop -->
  <rect x="30" y="140" width="340" height="14" rx="2" fill="url(#countertop)"/>

  <!-- Base Cabinets (Boiling Water Proof BWP Construction) -->
  <g transform="translate(35, 154)">
    <rect x="0" y="0" width="105" height="90" fill="url(#plyCarcass)" stroke="#9c784f" stroke-width="1.5"/>
    <line x1="10" y1="12" x2="95" y2="12" stroke="#64748b" stroke-width="2"/>
    <rect x="10" y="25" width="85" height="60" fill="#dfbe95" stroke="#9c784f" stroke-width="1"/>

    <rect x="110" y="0" width="105" height="90" fill="url(#plyCarcass)" stroke="#9c784f" stroke-width="1.5"/>
    <line x1="120" y1="12" x2="205" y2="12" stroke="#64748b" stroke-width="2"/>
    <rect x="120" y="25" width="85" height="60" fill="#dfbe95" stroke="#9c784f" stroke-width="1"/>

    <rect x="220" y="0" width="110" height="90" fill="url(#plyCarcass)" stroke="#9c784f" stroke-width="1.5"/>
    <line x1="230" y1="12" x2="320" y2="12" stroke="#64748b" stroke-width="2"/>
    <rect x="230" y="25" width="90" height="60" fill="#dfbe95" stroke="#9c784f" stroke-width="1"/>
  </g>

  <!-- Tag badge -->
  <g transform="translate(14, 256)">
    <rect width="180" height="28" rx="4" fill="#0f172a" opacity="0.85"/>
    <text x="12" y="18" fill="#a7f3d0" font-family="Inter, sans-serif" font-size="10.5" font-weight="600">BWP MODULAR CARCASS JOINERY</text>
  </g>
</svg>
`);

/**
 * 4. Calibrated Stack in Workshop / Atelier.
 */
export const PLYWOOD_STACK_SVG = encodeSvg(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300" width="400" height="300">
  <defs>
    <linearGradient id="shopBg" x1="0%" y1="0%" x2="0%" y2="100%">
      <stop offset="0%" stop-color="#2d3748"/>
      <stop offset="100%" stop-color="#1a202c"/>
    </linearGradient>
    <linearGradient id="topSheet" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#f5deb3"/>
      <stop offset="100%" stop-color="#d2a679"/>
    </linearGradient>
    <linearGradient id="stackEdge" x1="0%" y1="0%" x2="0%" y2="100%">
      <stop offset="0%" stop-color="#c89666"/>
      <stop offset="100%" stop-color="#8c5828"/>
    </linearGradient>
  </defs>

  <rect width="400" height="300" fill="url(#shopBg)"/>

  <!-- Workshop bench / floor -->
  <polygon points="0,220 400,220 400,300 0,300" fill="#171923"/>

  <!-- Isometric Stack of Plywood Panels -->
  <g transform="translate(50, 45)">
    <!-- Stack Left/Front Edge (Shows multi-ply banding across 20 stacked sheets) -->
    <polygon points="30,120 270,120 270,200 30,200" fill="url(#stackEdge)" stroke="#523213" stroke-width="1.5"/>
    
    <!-- Repeated horizontal lines representing individual sheets in stack -->
    <g stroke="#3b1f09" stroke-width="1" opacity="0.6">
      <line x1="30" y1="128" x2="270" y2="128"/>
      <line x1="30" y1="136" x2="270" y2="136"/>
      <line x1="30" y1="144" x2="270" y2="144"/>
      <line x1="30" y1="152" x2="270" y2="152"/>
      <line x1="30" y1="160" x2="270" y2="160"/>
      <line x1="30" y1="168" x2="270" y2="168"/>
      <line x1="30" y1="176" x2="270" y2="176"/>
      <line x1="30" y1="184" x2="270" y2="184"/>
      <line x1="30" y1="192" x2="270" y2="192"/>
    </g>

    <!-- Top Master Sheet Surface (Perspective Quad) -->
    <polygon points="30,120 100,40 340,40 270,120" fill="url(#topSheet)" stroke="#eed5b2" stroke-width="1"/>
    
    <!-- Top sheet wood grain flow -->
    <path d="M 50 105 Q 160 70, 310 50" stroke="#b0814a" stroke-width="1.5" fill="none" opacity="0.45"/>
    <path d="M 70 115 Q 190 75, 330 55" stroke="#9e6e37" stroke-width="1.8" fill="none" opacity="0.4"/>
    <path d="M 120 85 Q 210 65, 320 45" stroke="#b0814a" stroke-width="1.2" fill="none" opacity="0.35"/>

    <!-- Right Edge Profile -->
    <polygon points="270,120 340,40 340,120 270,200" fill="#935c2c" stroke="#523213" stroke-width="1.5"/>
    <g stroke="#3b1f09" stroke-width="1" opacity="0.6">
      <line x1="270" y1="128" x2="340" y2="48"/>
      <line x1="270" y1="136" x2="340" y2="56"/>
      <line x1="270" y1="144" x2="340" y2="64"/>
      <line x1="270" y1="152" x2="340" y2="72"/>
      <line x1="270" y1="160" x2="340" y2="80"/>
      <line x1="270" y1="168" x2="340" y2="88"/>
      <line x1="270" y1="176" x2="340" y2="96"/>
      <line x1="270" y1="184" x2="340" y2="104"/>
      <line x1="270" y1="192" x2="340" y2="112"/>
    </g>
  </g>

  <!-- Tag badge -->
  <g transform="translate(14, 256)">
    <rect width="186" height="28" rx="4" fill="#0f172a" opacity="0.85"/>
    <text x="12" y="18" fill="#fde047" font-family="Inter, sans-serif" font-size="10.5" font-weight="600">FACTORY CALIBRATED BATCH</text>
  </g>
</svg>
`);

/**
 * 5. Natural Decorative Teak Wood Veneer.
 */
export const PLYWOOD_TEAK_VENEER_SVG = encodeSvg(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300" width="400" height="300">
  <defs>
    <linearGradient id="teakBase" x1="0%" y1="0%" x2="100%" y2="0%">
      <stop offset="0%" stop-color="#935c26"/>
      <stop offset="25%" stop-color="#a86e33"/>
      <stop offset="50%" stop-color="#b67a3a"/>
      <stop offset="75%" stop-color="#8c531f"/>
      <stop offset="100%" stop-color="#7a4616"/>
    </linearGradient>
  </defs>
  
  <rect width="400" height="300" fill="url(#teakBase)"/>
  
  <!-- Longitudinal quarter-cut teak wood fibers -->
  <g stroke="#4f2b09" stroke-width="1.4" opacity="0.55">
    <line x1="30" y1="0" x2="32" y2="300"/>
    <line x1="45" y1="0" x2="44" y2="300" stroke-width="2.2"/>
    <line x1="70" y1="0" x2="72" y2="300"/>
    <line x1="95" y1="0" x2="93" y2="300" stroke-width="1.8"/>
    <line x1="120" y1="0" x2="124" y2="300"/>
    <line x1="140" y1="0" x2="138" y2="300" stroke-width="2.5"/>
    <line x1="175" y1="0" x2="176" y2="300"/>
    <line x1="200" y1="0" x2="198" y2="300" stroke-width="2.8"/>
    <line x1="230" y1="0" x2="234" y2="300"/>
    <line x1="265" y1="0" x2="262" y2="300" stroke-width="2.0"/>
    <line x1="290" y1="0" x2="292" y2="300"/>
    <line x1="320" y1="0" x2="318" y2="300" stroke-width="2.4"/>
    <line x1="350" y1="0" x2="354" y2="300"/>
    <line x1="375" y1="0" x2="373" y2="300" stroke-width="1.8"/>
  </g>

  <!-- Golden shimmer lines -->
  <g stroke="#ffda79" stroke-width="0.9" opacity="0.3">
    <line x1="55" y1="0" x2="56" y2="300"/>
    <line x1="108" y1="0" x2="106" y2="300"/>
    <line x1="160" y1="0" x2="162" y2="300"/>
    <line x1="215" y1="0" x2="216" y2="300"/>
    <line x1="278" y1="0" x2="276" y2="300"/>
    <line x1="335" y1="0" x2="337" y2="300"/>
  </g>

  <!-- Specular sheen -->
  <rect width="400" height="300" fill="none" stroke="#2a1403" stroke-width="3"/>
  <g transform="translate(14, 256)">
    <rect width="186" height="28" rx="4" fill="#1c1106" opacity="0.88"/>
    <text x="12" y="18" fill="#fef08a" font-family="Inter, sans-serif" font-size="10.5" font-weight="600">QUARTER-CUT TEAK VENEER</text>
  </g>
</svg>
`);

/**
 * 6. Phenolic Film-Faced Concrete Shuttering Plywood Board.
 */
export const PLYWOOD_SHUTTERING_SVG = encodeSvg(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300" width="400" height="300">
  <defs>
    <linearGradient id="filmBg" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#2b1408"/>
      <stop offset="45%" stop-color="#180b04"/>
      <stop offset="100%" stop-color="#0a0401"/>
    </linearGradient>
    <linearGradient id="glossReflection" x1="0%" y1="0%" x2="100%" y2="80%">
      <stop offset="0%" stop-color="#ffffff" stop-opacity="0.3"/>
      <stop offset="30%" stop-color="#ffffff" stop-opacity="0.08"/>
      <stop offset="70%" stop-color="#ffffff" stop-opacity="0.0"/>
    </linearGradient>
  </defs>

  <!-- Film-faced panel surface -->
  <rect width="400" height="300" fill="url(#filmBg)"/>
  
  <!-- Mirror gloss specular sheen -->
  <polygon points="0,0 260,0 120,300 0,300" fill="url(#glossReflection)"/>
  
  <!-- High visibility sealed red waterproof edge -->
  <rect x="0" y="0" width="400" height="300" fill="none" stroke="#dc2626" stroke-width="7"/>
  
  <!-- Formwork grid layout markings -->
  <g stroke="#ffffff" stroke-width="0.6" stroke-dasharray="4 6" opacity="0.25">
    <line x1="100" y1="0" x2="100" y2="300"/>
    <line x1="200" y1="0" x2="200" y2="300"/>
    <line x1="300" y1="0" x2="300" y2="300"/>
    <line x1="0" y1="100" x2="400" y2="100"/>
    <line x1="0" y1="200" x2="400" y2="200"/>
  </g>

  <!-- Stamp watermark -->
  <g transform="translate(14, 256)">
    <rect width="210" height="28" rx="4" fill="#000000" opacity="0.85"/>
    <text x="12" y="18" fill="#f87171" font-family="Inter, sans-serif" font-size="10.5" font-weight="600">120 GSM MIRROR FILM-FACED (BWP)</text>
  </g>
</svg>
`);

/**
 * Returns a high quality, guaranteed image URI based on role and product code/type.
 */
export function getPlywoodImageForRole(role: string, index = 0, productCode = ''): string {
  const code = productCode.toUpperCase();
  if (code.includes('VENEER')) {
    return index === 0 ? PLYWOOD_TEAK_VENEER_SVG : PLYWOOD_FACE_SHEET_SVG;
  }
  if (code.includes('SHUTTER')) {
    return index === 0 ? PLYWOOD_SHUTTERING_SVG : PLYWOOD_CROSS_SECTION_SVG;
  }

  switch (role) {
    case 'MAIN':
      return PLYWOOD_FACE_SHEET_SVG;
    case 'GALLERY':
      if (index === 1) return PLYWOOD_CROSS_SECTION_SVG;
      if (index === 2) return PLYWOOD_APPLICATION_KITCHEN_SVG;
      return PLYWOOD_STACK_SVG;
    default:
      return PLYWOOD_FACE_SHEET_SVG;
  }
}
