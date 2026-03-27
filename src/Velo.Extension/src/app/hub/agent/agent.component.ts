import { Component, OnInit, ViewChild, ElementRef, AfterViewChecked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgentConfigService, AgentConfigDto } from '../../shared/services/agent-config.service';
import { AgentService, ChatMessage } from '../../shared/services/agent.service';

@Component({
  selector: 'velo-agent',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './agent.component.html',
  styleUrls: ['./agent.component.scss'],
})
export class AgentComponent implements OnInit, AfterViewChecked {
  // Loading / config state
  isLoadingConfig = true;
  isConfigured = false;
  config: AgentConfigDto | null = null;

  // Connection form
  formEndpoint = '';
  formAgentId = '';
  formDisplayName = '';
  /** 'none' | 'apikey' | 'sp' */
  formAuthMethod: 'none' | 'apikey' | 'sp' = 'none';
  // Auth option 1 — API key
  formApiKey = '';
  // Auth option 2 — Service principal
  formTenantId = '';
  formClientId = '';
  formClientSecret = '';
  isTesting = false;
  isSaving = false;
  testResult: { ok: boolean; message: string } | null = null;
  saveError = '';

  // Chat state
  messages: ChatMessage[] = [];
  currentMessage = '';
  isThinking = false;
  selectedProjectId: string | null = null;

  @ViewChild('messagesContainer') messagesContainer!: ElementRef;
  private shouldScrollToBottom = false;

  constructor(
    private configService: AgentConfigService,
    private agentService: AgentService,
  ) {}

  ngOnInit(): void {
    this.selectedProjectId = sessionStorage.getItem('selectedProjectId');
    this.loadConfig();
  }

  ngAfterViewChecked(): void {
    if (this.shouldScrollToBottom) {
      this.scrollToBottom();
      this.shouldScrollToBottom = false;
    }
  }

  loadConfig(): void {
    this.isLoadingConfig = true;
    this.configService.getConfig().subscribe({
      next: (cfg) => {
        this.config = cfg;
        this.isConfigured = true;
        this.formEndpoint = cfg.foundryEndpoint;
        this.formAgentId = cfg.agentId ?? '';
        this.formDisplayName = cfg.displayName ?? '';
        // Restore auth method indicator from server flags
        if (cfg.hasApiKey) this.formAuthMethod = 'apikey';
        else if (cfg.hasServicePrincipal) this.formAuthMethod = 'sp';
        else this.formAuthMethod = 'none';
        this.isLoadingConfig = false;
      },
      error: (err) => {
        // 404 = not configured yet; anything else = real error (still show form)
        this.isConfigured = false;
        this.isLoadingConfig = false;
        if (err.status !== 404) {
          console.error('[AgentComponent] Error loading config:', err);
        }
      },
    });
  }

  testConnection(): void {
    if (!this.formEndpoint) return;
    this.isTesting = true;
    this.testResult = null;

    this.configService
      .testConnection({
        foundryEndpoint: this.formEndpoint,
        agentId: this.formAgentId || undefined,
        apiKey: this.formAuthMethod === 'apikey' ? (this.formApiKey || undefined) : undefined,
        tenantId: this.formAuthMethod === 'sp' ? (this.formTenantId || undefined) : undefined,
        clientId: this.formAuthMethod === 'sp' ? (this.formClientId || undefined) : undefined,
        clientSecret: this.formAuthMethod === 'sp' ? (this.formClientSecret || undefined) : undefined,
      })
      .subscribe({
        next: (res) => {
          this.testResult = { ok: true, message: res.message };
          this.isTesting = false;
        },
        error: (err) => {
          const msg = err.error?.message ?? 'Connection failed. Verify the endpoint URL and agent ID.';
          this.testResult = { ok: false, message: msg };
          this.isTesting = false;
        },
      });
  }

  saveConfig(): void {
    if (!this.formEndpoint) return;
    this.isSaving = true;
    this.saveError = '';

    const dto: AgentConfigDto = {
      id: this.config?.id ?? '00000000-0000-0000-0000-000000000000',
      orgId: '',
      foundryEndpoint: this.formEndpoint,
      agentId: this.formAgentId || undefined,
      displayName: this.formDisplayName || undefined,
      isEnabled: true,
      hasApiKey: false,
      hasServicePrincipal: false,
      // Only send credential fields for the selected auth method; blank = keep existing values
      apiKey: this.formAuthMethod === 'apikey' ? (this.formApiKey || undefined) : undefined,
      tenantId: this.formAuthMethod === 'sp' ? (this.formTenantId || undefined) : undefined,
      clientId: this.formAuthMethod === 'sp' ? (this.formClientId || undefined) : undefined,
      clientSecret: this.formAuthMethod === 'sp' ? (this.formClientSecret || undefined) : undefined,
    };

    this.configService.saveConfig(dto).subscribe({
      next: (saved) => {
        this.config = saved;
        this.isConfigured = true;
        this.isSaving = false;
      },
      error: (err) => {
        this.saveError = err.error?.error ?? 'Failed to save configuration. Please try again.';
        this.isSaving = false;
      },
    });
  }

  disconnectAgent(): void {
    if (!confirm('Disconnect the Foundry AI Agent? Your chat history will be cleared.')) return;

    this.configService.deleteConfig().subscribe({
      next: () => {
        this.config = null;
        this.isConfigured = false;
        this.messages = [];
        this.formEndpoint = '';
        this.formAgentId = '';
        this.formDisplayName = '';
        this.formAuthMethod = 'none';
        this.formApiKey = '';
        this.formTenantId = '';
        this.formClientId = '';
        this.formClientSecret = '';
        this.testResult = null;
      },
      error: (err) => console.error('[AgentComponent] Delete config failed:', err),
    });
  }

  sendMessage(): void {
    const msg = this.currentMessage.trim();
    if (!msg || this.isThinking || !this.selectedProjectId) return;

    // Add user message immediately for responsiveness
    this.messages.push({ role: 'user', content: msg });
    const historyToSend = this.messages.slice(0, -1); // exclude the message just pushed
    this.currentMessage = '';
    this.isThinking = true;
    this.shouldScrollToBottom = true;

    this.agentService
      .chat({
        projectId: this.selectedProjectId,
        message: msg,
        history: historyToSend,
      })
      .subscribe({
        next: (res) => {
          this.messages.push(res.message);
          this.isThinking = false;
          this.shouldScrollToBottom = true;
        },
        error: (err) => {
          const errMsg = err.error?.error ?? 'Something went wrong. Please try again.';
          this.messages.push({ role: 'assistant', content: `⚠️ ${errMsg}` });
          this.isThinking = false;
          this.shouldScrollToBottom = true;
        },
      });
  }

  sendSuggestion(text: string): void {
    this.currentMessage = text;
    this.sendMessage();
  }

  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendMessage();
    }
  }

  clearChat(): void {
    if (this.messages.length > 0 && !confirm('Clear the chat history?')) return;
    this.messages = [];
  }

  private scrollToBottom(): void {
    try {
      this.messagesContainer.nativeElement.scrollTop =
        this.messagesContainer.nativeElement.scrollHeight;
    } catch {
      // ignore
    }
  }
}
