import { handleEvaluatedEvent, readEnvelope } from './_shared.js';

async function main(): Promise<void> {
  try {
    handleEvaluatedEvent(await readEnvelope(), 'post');
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
