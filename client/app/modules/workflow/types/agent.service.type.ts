export enum AgentResponseStyle {
  CONCISE = "concise",
  BALANCED = "balanced",
  DETAILED = "detailed",
  PROFESSIONAL = "professional",
  CONVERSATIONAL = "conversational",
}

export enum AgentFormality {
  Formal = "formal",
  Balanced = "balanced",
  Casual = "casual",
}

export enum AgentTechnicalLevel {
  Basic = "basic",
  Intermediate = "intermediate",
  Advanced = "advanced",
  Expert = "expert",
}

export const AGENT_RESPONSE_STYLES: {
  label: string;
  value: AgentResponseStyle;
}[] = [
  { label: "Concise", value: AgentResponseStyle.CONCISE },
  { label: "Balanced", value: AgentResponseStyle.BALANCED },
  { label: "Detailed", value: AgentResponseStyle.DETAILED },
  { label: "Professional", value: AgentResponseStyle.PROFESSIONAL },
  { label: "Conversational", value: AgentResponseStyle.CONVERSATIONAL },
];
export enum WidgetType {
  CHAT = "chat",
  CALL = "call",
}
export type WidgetSetting = {
  widget_type: WidgetType;
  fe_style: string;
};

export type Widget = {
  name: string;
  widget_settings: WidgetSetting[];
  logo_url: string;
  logo_id: string | null;
  fe_script: string | null;
  show_agent_name: boolean;
  greeting: string;
  site_url: string | null;
  brand_color: string | null;
  predefined_questions: string[];
  enable_predefined_questions: boolean;
  // enable_questions_suggestions: boolean;
};

export type WidgetColorPalette = {
  "chat-header--background-start-color": string;
  "chat-header--background-end-color": string;
  "chat-header--title-color": string;
  "chat-window--background-color": string;
  "chat-window--font-color": string;
  "send-button--background-color": string;
  "send-button--font-color": string;
  "chat--border-color": string;
  "chat-toggle-button--background-color": string;
  "chat-toggle-button--color": string;
};

export type RagConfig = {
  top_k: number;
  score_threshold: number;
  embedding_model: string;
  embedding_model_provider: string;
  chunking_strategy: string;
  enable_query_enhancement: boolean;
  enable_rerank: boolean;
  dense_weight: number;
  keyword_weight: number;
};

// azure;
export type LLMConfig = {
  provider: string;
  model_name: string;
  is_blocks_provider: boolean;
  model_id?: string | null;
  model_type?: string;
  base_prompt?: string | null;
  api_key: string;
  api_base: string;
  api_version: string;
  temperature: number;
  max_tokens: number | null;
};

export type MemoryConfig = {
  last_n_conversations: number;
  enable_summary: boolean;
  is_blocks_provider: boolean;
  enable_questions_suggestions: boolean;
};

export type Guardrail = {
  enable_pii_detection: boolean;
  enable_injection_detection: boolean;
  enable_output_safety: boolean;
  enable_keyword_check: boolean;
  enable_custom_rules: boolean;

  enable_pre_validation: boolean;
  enable_post_validation: boolean;

  banned_keywords: string[];
  banned_patterns: string[];

  custom_rules: CustomRule[];

  input_risk_threshold: number;
  output_risk_threshold: number;
};

export type ViolationSeverity = "low" | "medium" | "high" | "critical";

export type CustomRule = {
  name: string;
  pattern: string;
  severity: string;
  auto_block: boolean;
  message?: string;
  enable_pre: boolean;
  enable_post: boolean;
};

export type Agent = {
  id: string;
  created_date: string;
  last_updated_date: string;
  created_by: string;
  language: string;
  last_updated_by: string;
  organization_ids: string[];
  tags: string[];
  name: string;
  agent_type: string | null;
  role: string;
  description: string;
  domain_expertise: string[];
  response_style: AgentResponseStyle;
  formality_level: AgentFormality;
  technical_level: AgentTechnicalLevel;
  enable_rag: boolean;
  rag_config: RagConfig;
  llm_config: LLMConfig;
  memory_config: MemoryConfig;
  widget_id: string;
  widget: Widget;
  logo_url: string | null;
  logo_id: string | null;
  is_disabled: boolean;
  is_archived: boolean;
  guardrail: Guardrail;
  enable_tools: boolean;
  tool_ids: string[];
  enable_knowledge_base: boolean;
  kb_ids: string[];
  folder_ids: string[];
  enable_memory: boolean;
  enable_human_handoff: boolean;
  published_date?: string;
  response_type: string;
  response_format: string | null;
  is_any_agent: boolean;
};

export type AgentSummary = Pick<
  Agent,
  | "id"
  | "name"
  | "tags"
  | "role"
  | "description"
  | "logo_url"
  | "logo_id"
  | "is_archived"
  | "is_disabled"
  | "widget_id"
>;

export interface IGetAgentsPayload {
  limit: number;
  offset: number;
  project_key: string;
  agent_type?: string;
  name?: string;
  organization_ids?: string[];
  widget_id?: string;
  languages?: string;
  tags?: string[];
  role?: string;
  is_archived?: boolean;
  is_disabled?: boolean;
}

export interface IGetAgentsResponse {
  agents: AgentSummary[];
  total_count: number;
}
