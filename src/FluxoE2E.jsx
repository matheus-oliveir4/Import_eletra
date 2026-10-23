import { useEffect, useState } from 'react';
import './FluxoE2E.css';
import defaultLogo from './eletra-logo.png';

/* ============================================================
   PRAZOS POR SEGMENTO (dias) — hoje iguais; ajuste quando tiver os números
   ============================================================ */
const SEGS = {
  rel:    { H: 120, sc: 8, po: 7, prodMts: 45, prodNon: 30, sea: 55, air: 10, clr: 10 },
  agua:   { H: 120, sc: 8, po: 7, prodMts: 45, prodNon: 30, sea: 55, air: 10, clr: 10 },
  energy: { H: 120, sc: 8, po: 7, prodMts: 45, prodNon: 30, sea: 55, air: 10, clr: 10 },
  newe:   { H: 120, sc: 8, po: 7, prodMts: 45, prodNon: 30, sea: 55, air: 10, clr: 10 },
};
const SEG_KEYS = ['rel', 'agua', 'energy', 'newe'];
const INTERVAL_MS = 4000;
const DAY = 86400000;

/* ---------- opções dos filtros ---------- */
const FIN_KEYS = ['desp', 'econs', 'einsc', 'einsp', 'erev', 'imoba', 'imob'];
const ST_OPTS = { canc: 'CANCELLED', cust: 'CUSTOMS CLEARANCE', deliv: 'DELIVERED', warr: 'WAITING ARRIVAL', wprod: 'WAITING PRODUCTION', wship: 'WAITING SHIPMENT' };
const GRP_OPTS = { energy: 'Energy', livoltek: 'Livoltek', recloser: 'Recloser', water: 'Water' };
const DRT_OPTS = { direct: 'Direct', indirect: 'Indirect' };
const CUR_OPTS = { arrived: 'Arrived Eletra', port: 'At Brazil Port / Customs', sea: 'On The Sea', finalized: 'Order Finalized', planning: 'Planning' };
const allOn = (keys) => Object.fromEntries(keys.map((k) => [k, true]));

/* ============================================================
   TEXTOS · pt = português, zh = chinês simplificado
   ============================================================ */
const I18N = {
  pt: {
    htmlLang: 'pt-BR', locale: 'pt-BR', other: '中文', otherAria: 'Ver versão em chinês',
    title: 'End-to-End', eyebrow: 'Supply chain Brasil ⇄ China · lead time end-to-end',
    phaseOf: (n) => (<>Fase <b>{n}</b> de 6</>),
    prev: 'Fase anterior', next: 'Próxima fase', pause: 'Pausar', play: 'Animar',
    product: 'PRODUTO', nonMts: 'Não MTS', mts: 'MTS', modal: 'MODAL', sea: 'Marítimo', air: 'Aéreo', d: 'd',
    days: (n) => `${n} dias`,
    total: 'LEAD TIME TOTAL', slack: 'FOLGA ATÉ D-0',
    slackPos: (n) => `+${n} dias`, slackZero: '0 dia · no limite', slackNeg: (n) => `${n} dias de atraso`,
    slackShort: (n) => (n >= 0 ? `folga ${n} d` : `atraso ${-n} d`),
    brazil: 'Brasil', china: 'China',
    dirBr: (n) => `T0 – T2 · FLUXO DE INFORMAÇÃO E COMPRA · ${n} DIAS`,
    dirCn: (n) => `T3 – T5 · FLUXO FÍSICO DA CARGA · ${n} DIAS`,
    active: 'Em curso', done: 'Concluída', upcoming: 'Próxima', from: 'DE', to: 'ATÉ',
    horizon: (m) => `Horizonte de abastecimento · ${m} meses`,
    horizonSub: (h) => `D-${h} plano comercial entregue → D-0 data de necessidade na fábrica`,
    slackSeg: 'Folga', need: 'necessidade', today: 'HOJE',
    activePhase: 'Fase ativa', pillBr: 'BRASIL → CHINA', pillCn: 'CHINA → BRASIL', deadline: 'Prazo',
    start: 'INÍCIO', end: 'FIM', rule: 'RESPONSÁVEL · REGRA', milestones: 'Marcos e datas-limite',
    filters: 'Filtros', segment: 'SEGMENTO', finLabel: 'FINALIDADE', stLabel: 'STATUS', grpLabel: 'GROUP', drtLabel: 'DIRECT',
    curLabel: 'CURRENT STATUS', needDate: 'DATA DE NECESSIDADE', scenario: 'CENÁRIO', onTime: 'No prazo', late: 'Atraso',
    all: 'Todos', none: 'Nenhum', count: (a, b) => `${a} de ${b} selecionados`, many: (n) => `${n} selecionados`,
    segNames: { rel: 'Religadores', agua: 'Água', energy: 'Energy', newe: 'New Energy' },
    fin: { desp: 'Despesa', econs: 'Estoque Consumo', einsc: 'Estoque Insumos (Componente)', einsp: 'Estoque Insumos (Policarbonato)', erev: 'Estoque Revenda', imoba: 'Imob. Andamento', imob: 'Imobilizado' },
    ms: (air) => ['Plano comercial entregue', 'Solicitação de Compra emitida', 'SC aprovada pelo PCM', 'PO enviada · início da produção',
      'Embarque na China', air ? 'Chegada no aeroporto' : 'Chegada no porto', 'Chegada na empresa'],
    phases: (x) => [
      { name: 'Plano comercial', from: 'Plano comercial', to: 'Solicitação de Compra', owner: 'Comercial',
        title: 'Plano comercial', dFrom: 'Plano comercial', dTo: 'Solicitação de Compra (SC)', win: `Entrega em D-${x.H}`,
        rule: `Comercial · entregar o plano ${x.meses} meses antes da data de necessidade da matéria-prima na fábrica.`,
        docs: ['Plano comercial', 'Data de necessidade', 'Solicitação de Compra'] },
      { name: 'Aprovação da SC', from: 'Solicitação de Compra pelo PCM', to: 'Purchase Order', owner: 'PCM',
        title: 'Aprovação da Solicitação de Compra', dFrom: 'SC emitida pelo PCM', dTo: 'Purchase Order',
        rule: `PCM · ${x.sc} dias para aprovar a Solicitação de Compra.`, docs: ['Solicitação de Compra', 'Aprovação PCM'] },
      { name: 'Emissão da PO', from: 'Criação e envio da PO', to: 'Início da produção', owner: 'Comex',
        title: 'Emissão da Purchase Order', dFrom: 'Criação e envio da PO', dTo: 'Início da produção',
        rule: `Comex · ${x.po} dias para criar, aprovar e enviar a PO à HQ na China.`, docs: ['Purchase Order', 'Aprovação', 'Envio à HQ'] },
      { name: 'Produção HQ', from: 'Produção na HQ', to: 'Embarque', owner: `HQ · ${x.mts ? 'MTS' : 'Não MTS'}`,
        title: 'Produção na HQ', dFrom: 'Início da produção', dTo: 'Embarque',
        rule: `HQ · ${x.prodMts} dias para produtos MTS (Make to Stock) e ${x.prodNon} dias para produtos não MTS.`,
        docs: ['Order confirmation', 'Commercial invoice', 'Packing list'] },
      { name: x.air ? 'Transporte aéreo' : 'Transporte marítimo', from: 'Embarque na China', to: x.air ? 'Chegada no aeroporto' : 'Chegada no porto',
        owner: `Transp. · ${x.air ? 'AIR' : 'SEA'}`,
        title: `${x.air ? 'Transporte aéreo' : 'Transporte marítimo'} China → Brasil`, dFrom: 'Embarque na China',
        dTo: x.air ? 'Chegada no aeroporto' : 'Chegada no porto',
        rule: `Transportadora · ${x.seaD} dias via marítima (SEA) ou ${x.airD} dias via aérea (AIR).`,
        docs: [x.air ? 'AWB' : 'Bill of Lading', 'Tracking', 'Aviso de chegada'] },
      { name: 'Desembaraço e entrega', from: 'Desembarque e desembaraço', to: 'Chegada na empresa', owner: 'Pós-embarque',
        title: 'Desembaraço aduaneiro e entrega', dFrom: 'Desembarque', dTo: 'Chegada na empresa',
        rule: `Pós-embarque · ${x.clr} dias após o desembarque para desembaraçar e entregar na empresa.`,
        docs: ['DUIMP / DI', 'Liberação aduaneira', 'NF de entrada'] },
    ],
  },

  zh: {
    htmlLang: 'zh-CN', locale: 'zh-CN', other: 'PT', otherAria: '查看葡萄牙语版本',
    title: '端到端流程', eyebrow: '供应链 巴西 ⇄ 中国 · 端到端交付周期',
    phaseOf: (n) => (<>第 <b>{n}</b> 阶段 / 共 6 个</>),
    prev: '上一阶段', next: '下一阶段', pause: '暂停', play: '播放',
    product: '产品类型', nonMts: '非 MTS', mts: 'MTS', modal: '运输方式', sea: '海运', air: '空运', d: '天',
    days: (n) => `${n} 天`,
    total: '总交付周期', slack: '距 D-0 余量',
    slackPos: (n) => `+${n} 天`, slackZero: '0 天 · 刚好到期', slackNeg: (n) => `延误 ${n} 天`,
    slackShort: (n) => (n >= 0 ? `余量 ${n} 天` : `延误 ${-n} 天`),
    brazil: '巴西', china: '中国',
    dirBr: (n) => `T0 – T2 · 信息与采购流 · ${n} 天`,
    dirCn: (n) => `T3 – T5 · 货物实物流 · ${n} 天`,
    active: '进行中', done: '已完成', upcoming: '待开始', from: '起', to: '止',
    horizon: (m) => `供应周期 · ${m} 个月`,
    horizonSub: (h) => `D-${h} 提交商业计划 → D-0 工厂需求日`,
    slackSeg: '余量', need: '需求日', today: '今天',
    activePhase: '当前阶段', pillBr: '巴西 → 中国', pillCn: '中国 → 巴西', deadline: '时限',
    start: '开始', end: '结束', rule: '负责方 · 规则', milestones: '里程碑与截止日期',
    filters: '筛选', segment: '业务板块', finLabel: '用途', stLabel: '状态', grpLabel: '集团', drtLabel: '直接/间接',
    curLabel: '当前状态', needDate: '需求日期', scenario: '情景', onTime: '按期', late: '延误',
    all: '全部', none: '未选择', count: (a, b) => `已选 ${a} / ${b}`, many: (n) => `已选 ${n} 项`,
    segNames: { rel: '重合器', agua: '水务', energy: '能源', newe: '新能源' },
    fin: { desp: '费用', econs: '消耗品库存', einsc: '原材料库存（元器件）', einsp: '原材料库存（聚碳酸酯）', erev: '转售库存', imoba: '在建固定资产', imob: '固定资产' },
    ms: (air) => ['提交商业计划', '发出采购申请', 'PCM 批准采购申请', '发送 PO · 开始生产', '中国装运', air ? '到达机场' : '到达港口', '到达公司'],
    phases: (x) => [
      { name: '商业计划', from: '商业计划', to: '采购申请 (SC)', owner: '商务部',
        title: '商业计划', dFrom: '商业计划', dTo: '采购申请 (SC)', win: `D-${x.H} 提交`,
        rule: `商务部 · 须在工厂原材料需求日前 ${x.meses} 个月提交商业计划。`, docs: ['商业计划', '需求日期', '采购申请'] },
      { name: '采购申请审批', from: 'PCM 发起采购申请', to: '采购订单 (PO)', owner: 'PCM',
        title: '采购申请审批', dFrom: 'PCM 发起采购申请', dTo: '采购订单 (PO)',
        rule: `PCM · ${x.sc} 天内完成采购申请审批。`, docs: ['采购申请', 'PCM 审批'] },
      { name: '下达 PO', from: '创建并发送 PO', to: '开始生产', owner: '进出口部',
        title: '下达采购订单 (PO)', dFrom: '创建并发送 PO', dTo: '开始生产',
        rule: `进出口部 (Comex) · ${x.po} 天内创建、审批并向中国总部发送 PO。`, docs: ['采购订单', '审批', '发送至总部'] },
      { name: '总部生产', from: '总部生产', to: '装运', owner: `总部 · ${x.mts ? 'MTS' : '非 MTS'}`,
        title: '总部生产', dFrom: '开始生产', dTo: '装运',
        rule: `总部 (HQ) · MTS（按库存生产）产品 ${x.prodMts} 天，非 MTS 产品 ${x.prodNon} 天。`, docs: ['订单确认', '商业发票', '装箱单'] },
      { name: x.air ? '空运运输' : '海运运输', from: '中国装运', to: x.air ? '到达机场' : '到达港口', owner: `承运商 · ${x.air ? 'AIR' : 'SEA'}`,
        title: `${x.air ? '空运' : '海运'} 中国 → 巴西`, dFrom: '中国装运', dTo: x.air ? '到达机场' : '到达港口',
        rule: `承运商 · 海运 (SEA) ${x.seaD} 天，空运 (AIR) ${x.airD} 天。`, docs: [x.air ? '空运单 (AWB)' : '提单 (B/L)', '货物跟踪', '到货通知'] },
      { name: '清关与交付', from: '卸货与清关', to: '到达公司', owner: '到港后团队',
        title: '清关与交付', dFrom: '卸货', dTo: '到达公司',
        rule: `到港后团队 · 卸货后 ${x.clr} 天内完成清关并送达公司。`, docs: ['进口申报 (DUIMP / DI)', '海关放行', '入库发票 (NF)'] },
    ],
  },
};

const COL = { lime: '#7FA51C', blue: '#0A86BF', red: '#B42318' };
const ICONS_BY_PHASE = ['plan', 'cart', 'send', 'factory', null, 'truck'];
const SEG_ICON = { rel: 'power', agua: 'drop', energy: 'bolt', newe: 'sun' };

const ICON = {
  plan: (<><rect x="5" y="4" width="14" height="17" rx="2" /><path d="M9 3h6v3H9z" /><path d="M9 12h6M9 16h4" /></>),
  cart: (<><circle cx="9" cy="20" r="1.5" /><circle cx="17" cy="20" r="1.5" /><path d="M3 4h2l2.4 11h11l2-8H6.2" /></>),
  send: (<><path d="M21 3L10 14" /><path d="M21 3l-7 18-4-7-7-4z" /></>),
  factory: (<><path d="M3 21V10l6 4v-4l6 4V6l6 3v12z" /><path d="M3 21h18" /></>),
  ship: (<><path d="M3 15h18l-2.5 5h-13z" /><path d="M6 15V9h12v6" /><path d="M12 9V4" /><path d="M9 6h6" /></>),
  plane: (<path d="M2 13l8-2 4-7h2l-2 7 6 1 2-3h1.5l-1 4 1 4H22l-2-3-6 1 2 7h-2l-4-7-8-2z" />),
  truck: (<><path d="M2 6h11v10H2z" /><path d="M13 9h4l4 4v3h-8" /><circle cx="6.5" cy="18" r="1.8" /><circle cx="17" cy="18" r="1.8" /></>),
  pause: (<path d="M8 5v14M16 5v14" />),
  play: (<path d="M7 4l13 8-13 8z" />),
  prev: (<path d="M15 18l-6-6 6-6" />),
  next: (<path d="M9 18l6-6-6-6" />),
  chev: (<path d="M6 9l6 6 6-6" />),
  power: (<><path d="M12 2v9" /><path d="M18.4 6.6a9 9 0 1 1-12.8 0" /></>),
  drop: (<path d="M12 2.7s-6 6.6-6 11.3a6 6 0 0 0 12 0c0-4.7-6-11.3-6-11.3z" />),
  bolt: (<path d="M13 2L4 14h7l-1 8 9-12h-7z" />),
  sun: (<><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></>),
};

function Icon({ name, size = 20, className }) {
  return (
    <svg className={className} width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {ICON[name]}
    </svg>
  );
}

function Arrow({ color, w = 34, h = 14 }) {
  return (
    <svg width={w} height={h} viewBox={`0 0 ${w} ${h}`} fill="none" stroke={color} strokeWidth="2.2"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d={`M2 ${h / 2}h${w - 6}M${w - 10} 2l6 ${h / 2 - 2}-6 ${h / 2 - 2}`} />
    </svg>
  );
}

/* Menu suspenso de seleção múltipla com "Todos" */
function MultiSelect({ id, label, options, value, onChange, open, onToggle, t }) {
  const keys = Object.keys(options);
  const sel = keys.filter((k) => value[k]);
  const all = sel.length === keys.length;
  const summary = all ? t.all : sel.length === 0 ? t.none : sel.length === 1 ? options[sel[0]] : t.many(sel.length);
  const setAll = () => onChange(Object.fromEntries(keys.map((k) => [k, !all])));
  return (
    <div className="flt">
      <span id={`${id}-label`} className="flt-lbl">{label}</span>
      <button type="button" className={`dd${open ? ' open' : ''}`} onClick={onToggle} aria-expanded={open} aria-labelledby={`${id}-label`}>
        <span className="main"><span className="v">{summary}</span><span className="s">{t.count(sel.length, keys.length)}</span></span>
        <Icon name="chev" size={18} className="chev" />
      </button>
      {open && (
        <div className="pop" role="group" aria-labelledby={`${id}-label`}>
          <label className="chk all"><input type="checkbox" checked={all} onChange={setAll} /><span>{t.all}</span></label>
          <div className="sep" />
          {keys.map((k) => (
            <label className="chk" key={k}>
              <input type="checkbox" checked={!!value[k]} onChange={() => onChange({ ...value, [k]: !value[k] })} />
              <span>{options[k]}</span>
            </label>
          ))}
        </div>
      )}
    </div>
  );
}

const dl = (v) => (v > 0 ? `D-${v}` : v === 0 ? 'D-0' : `D+${-v}`);
const center = (k) => `(${(((2 * k + 1) / 12) * 100).toFixed(4)}% + ${(((2 * k - 5) * 16) / 12).toFixed(3)}px)`;
const totalOf = (x, mts, air) => x.sc + x.po + (mts ? x.prodMts : x.prodNon) + (air ? x.air : x.sea) + x.clr;

/**
 * Props:
 *  - lang:    idioma inicial, 'pt' (padrão) | 'zh'
 *  - logoSrc: caminho do logo (padrão: eletra-logo.png importado)
 */
export default function FluxoE2E({ lang: initialLang = 'pt', logoSrc = defaultLogo }) {
  const [lang, setLang] = useState(initialLang === 'zh' ? 'zh' : 'pt');
  const t = I18N[lang];

  const [i, setI] = useState(0);
  const [playing, setPlaying] = useState(true);
  const [mts, setMts] = useState(false);
  const [air, setAir] = useState(false);
  const [seg, setSeg] = useState('rel');
  const [openDd, setOpenDd] = useState(null);
  const [need, setNeed] = useState('');
  const [fin, setFin] = useState(allOn(FIN_KEYS));
  const [st, setSt] = useState(allOn(Object.keys(ST_OPTS)));
  const [grp, setGrp] = useState(allOn(Object.keys(GRP_OPTS)));
  const [drt, setDrt] = useState(allOn(Object.keys(DRT_OPTS)));
  const [cur, setCur] = useState(allOn(Object.keys(CUR_OPTS)));

  useEffect(() => {
    if (!playing) return undefined;
    const id = setInterval(() => setI((v) => (v + 1) % 6), INTERVAL_MS);
    return () => clearInterval(id);
  }, [playing]);

  useEffect(() => { document.documentElement.lang = t.htmlLang; }, [t.htmlLang]);

  const goTo = (k) => { setI(k); setPlaying(false); };
  const toggleDd = (k) => setOpenDd((o) => (o === k ? null : k));

  /* ---------- cálculos ---------- */
  const S = SEGS[seg];
  const H = S.H, sc = S.sc, po = S.po, clr = S.clr;
  const prod = mts ? S.prodMts : S.prodNon;
  const tr = air ? S.air : S.sea;
  const total = sc + po + prod + tr + clr;
  const slack = H - total;
  const m = [H, H, H - sc];
  m.push(m[2] - po); m.push(m[3] - prod); m.push(m[4] - tr); m.push(m[5] - clr);
  const meses = Math.round(H / 30);

  /* datas reais a partir da data de necessidade */
  const now = new Date();
  const todayMs = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  let needMs = need ? Date.parse(`${need}T00:00:00Z`) : NaN;
  if (Number.isNaN(needMs)) needMs = todayMs + 150 * DAY;
  const needISO = new Date(needMs).toISOString().slice(0, 10);
  const at = (v) => new Date(needMs - v * DAY);
  const fmt = (v) => at(v).toLocaleDateString(t.locale, { day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'UTC' });
  const fmtS = (v) => at(v).toLocaleDateString(t.locale, { day: '2-digit', month: '2-digit', timeZone: 'UTC' });
  const todayD = Math.round((needMs - todayMs) / DAY);

  const texts = t.phases({ H, meses, sc, po, clr, mts, air, prodMts: S.prodMts, prodNon: S.prodNon, seaD: S.sea, airD: S.air });
  const lts = [`D-${H}`, t.days(sc), t.days(po), t.days(prod), t.days(tr), t.days(clr)];
  const P = texts.map((p, k) => ({
    ...p,
    code: `T${k}`,
    icon: k === 4 ? (air ? 'plane' : 'ship') : ICONS_BY_PHASE[k],
    lt: lts[k],
    win: k === 0 ? p.win : `${dl(m[k])} → ${dl(m[k + 1])}`,
    dates: k === 0 ? fmt(H) : `${fmt(m[k])} → ${fmt(m[k + 1])}`,
  }));
  const ph = P[i];
  const br = i <= 2;

  /* régua */
  const Hd = Math.max(H, total);
  const pos = (v) => `${(((H - v) / Hd) * 100).toFixed(3)}%`;
  const durs = [sc, po, prod, tr, clr];
  const ticks = [{ label: `D-${H} · ${fmtS(H)}`, left: '0%', tf: 'none' }];
  for (let v = H - 30; v > 0; v -= 30) ticks.push({ label: `D-${v} · ${fmtS(v)}`, left: pos(v), tf: 'translateX(-50%)' });
  if (slack < 0) {
    ticks.push({ label: `D-0 ${fmtS(0)} → D+${-slack} ${fmtS(slack)}`, left: '100%', tf: 'translateX(-100%)', color: 'var(--red)' });
  } else {
    ticks.push({ label: `D-0 · ${fmt(0)} · ${t.need}`, left: '100%', tf: 'translateX(-100%)', color: 'var(--ink)' });
  }
  const slackPct = (Math.max(0, slack) / Hd) * 100;
  const showToday = todayD <= H && todayD >= Math.min(0, slack);
  const msLabels = t.ms(air);
  const scenarioLabel = `${mts ? t.mts : t.nonMts} · ${air ? t.air : t.sea}`;

  const finSel = FIN_KEYS.filter((k) => fin[k]);
  const finSummary = finSel.length === FIN_KEYS.length ? t.all : finSel.length === 0 ? t.none : finSel.length === 1 ? t.fin[finSel[0]] : t.many(finSel.length);

  return (
    <div id="e2e-fluxo" lang={t.htmlLang} className={`lang-${lang}`} style={{ '--marker': br ? COL.lime : COL.blue }}>
      <div className="shell">

        {/* ================= Barra lateral ================= */}
        <aside className="side" aria-label={t.filters}>
          <div className="logo-wrap"><img className="logo" src={logoSrc} alt="Eletra Energy Solutions" /></div>

          {/* Segmento (seleção única) */}
          <div className="flt">
            <span id="seg-label" className="flt-lbl">{t.segment}</span>
            <button type="button" className={`dd${openDd === 'seg' ? ' open' : ''}`} onClick={() => toggleDd('seg')} aria-haspopup="listbox" aria-expanded={openDd === 'seg'} aria-labelledby="seg-label">
              <span className="ib on"><Icon name={SEG_ICON[seg]} size={20} /></span>
              <span className="main"><span className="v">{t.segNames[seg]}</span><span className="s">{t.days(total)} · {t.slackShort(slack)}</span></span>
              <Icon name="chev" size={18} className="chev" />
            </button>
            {openDd === 'seg' && (
              <div className="pop" role="listbox" aria-labelledby="seg-label">
                {SEG_KEYS.map((k) => {
                  const sl = SEGS[k].H - totalOf(SEGS[k], mts, air);
                  return (
                    <button type="button" key={k} role="option" aria-selected={k === seg} className={`opt${k === seg ? ' on' : ''}`}
                      onClick={() => { setSeg(k); setI(0); setOpenDd(null); }}>
                      <span className="ib"><Icon name={SEG_ICON[k]} size={18} /></span>
                      <span className="main"><span className="v">{t.segNames[k]}</span><span className="s">{t.days(totalOf(SEGS[k], mts, air))} · {t.slackShort(sl)}</span></span>
                      <span className="dot8" style={{ background: sl >= 0 ? COL.lime : COL.red }} />
                    </button>
                  );
                })}
              </div>
            )}
          </div>

          <MultiSelect id="fin" label={t.finLabel} options={t.fin} value={fin} onChange={setFin} open={openDd === 'fin'} onToggle={() => toggleDd('fin')} t={t} />
          <MultiSelect id="st" label={t.stLabel} options={ST_OPTS} value={st} onChange={setSt} open={openDd === 'st'} onToggle={() => toggleDd('st')} t={t} />
          <MultiSelect id="grp" label={t.grpLabel} options={GRP_OPTS} value={grp} onChange={setGrp} open={openDd === 'grp'} onToggle={() => toggleDd('grp')} t={t} />
          <MultiSelect id="drt" label={t.drtLabel} options={DRT_OPTS} value={drt} onChange={setDrt} open={openDd === 'drt'} onToggle={() => toggleDd('drt')} t={t} />
          <MultiSelect id="cur" label={t.curLabel} options={CUR_OPTS} value={cur} onChange={setCur} open={openDd === 'cur'} onToggle={() => toggleDd('cur')} t={t} />

          <div className="flt">
            <label htmlFor="need-date" className="flt-lbl">{t.needDate}</label>
            <input id="need-date" className="date" type="date" value={needISO} onChange={(e) => setNeed(e.target.value)} />
          </div>

          <div className="scen-card">
            <span className="flt-lbl" style={{ padding: 0 }}>{t.scenario}</span>
            <span className="sv">{scenarioLabel}</span>
            <div className="legend">
              <span><span className="dot8" style={{ background: COL.lime }} />{t.onTime}</span>
              <span><span className="dot8" style={{ background: COL.red }} />{t.late}</span>
            </div>
          </div>
        </aside>

        {/* ================= Conteúdo ================= */}
        <main className="content">
          <header className="hdr">
            <div className="ttl">
              <div className="eyebrow mono"><span className="blink" /><span>{t.eyebrow}</span></div>
              <div className="ttl-row">
                <h1>{t.title}</h1>
                <span className="chip seg">{t.segNames[seg]}</span>
                <span className="chip fin"><span className="mono k">{t.finLabel}</span>{finSummary}</span>
              </div>
            </div>
            <div className="ctls">
              <button type="button" className="ctl lang" aria-label={t.otherAria} onClick={() => setLang(lang === 'pt' ? 'zh' : 'pt')}>{t.other}</button>
              <span className="mono counter">{t.phaseOf(i + 1)}</span>
              <button type="button" className="ctl sq" aria-label={t.prev} onClick={() => goTo((i + 5) % 6)}><Icon name="prev" size={18} /></button>
              <button type="button" className="ctl play" onClick={() => setPlaying((p) => !p)}>
                <Icon name={playing ? 'pause' : 'play'} size={16} /><span>{playing ? t.pause : t.play}</span>
              </button>
              <button type="button" className="ctl sq" aria-label={t.next} onClick={() => goTo((i + 1) % 6)}><Icon name="next" size={18} /></button>
            </div>
          </header>

          <div className="scen">
            <div className="grp">
              <span className="mono lbl">{t.product}</span>
              <div className="segctl">
                <button type="button" aria-pressed={!mts} onClick={() => setMts(false)}>{t.nonMts} · {S.prodNon} {t.d}</button>
                <button type="button" aria-pressed={mts} onClick={() => setMts(true)}>{t.mts} · {S.prodMts} {t.d}</button>
              </div>
            </div>
            <div className="grp">
              <span className="mono lbl">{t.modal}</span>
              <div className="segctl">
                <button type="button" aria-pressed={!air} onClick={() => setAir(false)}>{t.sea} · {S.sea} {t.d}</button>
                <button type="button" aria-pressed={air} onClick={() => setAir(true)}>{t.air} · {S.air} {t.d}</button>
              </div>
            </div>
            <div className="kpis">
              <div className="kpi"><span className="mono lbl">{t.total}</span><span className="v">{t.days(total)}</span></div>
              <div className={`kpi ${slack >= 0 ? 'ok' : 'late'}`}>
                <span className="mono lbl">{t.slack}</span>
                <span className="v">{slack > 0 ? t.slackPos(slack) : slack === 0 ? t.slackZero : t.slackNeg(-slack)}</span>
              </div>
            </div>
          </div>

          <div className="grid6">
            <div className={`dir br${br ? ' on' : ''}`}>
              <div className="route">{t.brazil} <Arrow color="#7FA51C" /> {t.china}</div>
              <span className="mono lbl">{t.dirBr(sc + po)}</span>
            </div>
            <div className={`dir cn${!br ? ' on' : ''}`}>
              <div className="route">{t.china} <Arrow color="#19B4E0" /> {t.brazil}</div>
              <span className="mono lbl">{t.dirCn(prod + tr + clr)}</span>
            </div>
          </div>

          <div className="rail">
            <div className="base" style={{ left: `calc${center(0)}`, right: `calc(100% - ${center(5)})` }} />
            <div className="prog" style={{ left: `calc${center(0)}`, width: `calc(${center(i)} - ${center(0)})` }} />
            <div className="dots grid6">
              {[0, 1, 2, 3, 4, 5].map((k) => {
                const c = k <= 2 ? COL.lime : COL.blue;
                return <div key={k}><div className="dot" style={{ borderColor: k <= i ? c : '#C3D4DD', background: k < i ? c : undefined }} /></div>;
              })}
            </div>
            <div className="marker" style={{ left: `calc${center(i)}` }}>
              <div className="ring" />
              <div className="disc"><Icon name={ph.icon} size={26} /></div>
            </div>
          </div>

          <div className="grid6">
            {P.map((p, k) => (
              <button type="button" key={p.code} onClick={() => goTo(k)}
                className={`card ${k <= 2 ? 'side-br' : 'side-cn'}${k === i ? ' is-active' : ''}${k < i ? ' is-done' : ''}`}>
                <div className="bar" />
                <div className="top">
                  <span className="mono code">{p.code}</span>
                  <span className="mono status">{k === i ? t.active : k < i ? t.done : t.upcoming}</span>
                </div>
                <div className="head"><div className="ico"><Icon name={p.icon} /></div><div className="name">{p.name}</div></div>
                <div className="ft">
                  <div><span className="k">{t.from}</span><span>{p.from}</span></div>
                  <div><span className="k">{t.to}</span><span>{p.to}</span></div>
                </div>
                <div className="foot"><span className="mono owner">{p.owner}</span><span className="lt">{p.lt}</span></div>
              </button>
            ))}
          </div>

          <div className="panel ruler">
            <div className="hd">
              <span className="mono eyebrow">{t.horizon(meses)}</span>
              <span className="sub">{t.horizonSub(H)}</span>
            </div>
            <div className="bars">
              {durs.map((d, s) => {
                const k = s + 1, pct = (d / Hd) * 100;
                return (
                  <div key={k} className={`sg ${k <= 2 ? 'br' : 'cn'}${k === i ? ' active' : ''}${k < i ? ' done' : ''}`} style={{ width: `${pct.toFixed(3)}%` }}>
                    {pct >= 6 ? `T${k} · ${d}${t.d}` : `T${k}`}
                  </div>
                );
              })}
              <div className="sg slack" style={{ width: `${slackPct.toFixed(3)}%` }}>
                {slack > 0 ? (slackPct >= 8 ? `${t.slackSeg} · ${slack}${t.d}` : `${slack}${t.d}`) : ''}
              </div>
              {slack < 0 && <div className="over" style={{ display: 'flex', left: pos(0) }}>+{-slack}{t.d}</div>}
              {showToday && (
                <>
                  <div className="today" style={{ left: pos(todayD) }} aria-hidden="true" />
                  <span className="today-tag" style={{ left: pos(todayD) }}>{t.today}</span>
                </>
              )}
            </div>
            <div className="ticks">
              {ticks.map((tk) => (
                <span key={tk.label} style={{ left: tk.left, transform: tk.tf, color: tk.color }}>{tk.label}</span>
              ))}
            </div>
          </div>

          <div className="bottom">
            <div className="panel detail">
              <div className="l">
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                  <span className="mono eyebrow">{t.activePhase}</span>
                  <span className={`pill ${br ? 'br' : 'cn'}`}>{br ? t.pillBr : t.pillCn}</span>
                </div>
                <div className="bigcode">{ph.code}</div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                  <span className="mono lbl" style={{ textTransform: 'uppercase' }}>{t.deadline}</span>
                  <span style={{ fontSize: 22, fontWeight: 700 }}>{ph.lt}</span>
                </div>
              </div>
              <div className="r">
                <div className="tt">
                  <div className="t">{ph.title}</div>
                  <span className="w2"><span className="dates">{ph.dates}</span><span className="w mono">{ph.win}</span></span>
                </div>
                <div className="fromto">
                  <div className="box"><div className="mono lbl">{t.start}</div><div className="v">{ph.dFrom}</div></div>
                  <Arrow color="#19B4E0" w={28} h={16} />
                  <div className="box end"><div className="mono lbl">{t.end}</div><div className="v">{ph.dTo}</div></div>
                </div>
                <div><div className="mono lbl">{t.rule}</div><div className="rule">{ph.rule}</div></div>
                <div className="docs">{ph.docs.map((d) => <span key={d}>{d}</span>)}</div>
              </div>
            </div>

            <div className="panel ms">
              <div className="hd">
                <span className="mono eyebrow">{t.milestones}</span>
                <span className="mono lbl">D-0 = {fmt(0)}</span>
              </div>
              <ul>
                {msLabels.map((label, k) => {
                  const cls = [k <= i ? 'reached' : k === i + 1 ? 'target' : '', k === 6 ? `final ${slack >= 0 ? 'ok' : 'late'}` : ''].filter(Boolean).join(' ');
                  return (
                    <li key={k} className={cls} style={{ '--c': k <= 3 ? COL.lime : COL.blue }}>
                      <span className="b" /><span className="id">M{k}</span><span className="n">{label}</span>
                      <span className="dd2"><span className="dx">{dl(m[k])}</span><span className="d">{fmt(m[k])}</span></span>
                    </li>
                  );
                })}
              </ul>
            </div>
          </div>
        </main>
      </div>
    </div>
  );
}
