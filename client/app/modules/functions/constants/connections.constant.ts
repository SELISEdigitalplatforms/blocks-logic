/**
 * Ready-made setups for the services a function most often talks to. Each one names the npm
 * package, the variables its connection needs, and a handler that opens, uses and closes the
 * connection within one run — every run is a fresh sandbox, so nothing can be pooled across calls.
 *
 * Only public endpoints are reachable from a sandbox: private ranges, loopback and the VPN are
 * dropped by the runner's egress firewall, which is why every hint asks for a hosted service.
 */
export interface IConnectionVariable {
  key: string;
  /** A credential: bind it to a configuration variable rather than typing it in plain. */
  secret: boolean;
  hint: string;
  /** Filled in from the project when the connection is added, instead of left empty. */
  prefill?: ConnectionPrefill;
  /**
   * Plain setting a connection starts with (a queue or topic name) when the project has nothing to
   * prefill it from. Never set on a secret: a credential is always bound, never shipped as a default.
   */
  defaultValue?: string;
}

/** Values the console knows about the project and can fill in for the user. */
export type ConnectionPrefill = "blocksApiHost";
export type ConnectionPrefills = Partial<Record<ConnectionPrefill, string>>;

export interface IConnectionPreset {
  id: string;
  label: string;
  description: string;
  packageName: string;
  /**
   * Fallback only: adding a connection pins npm's current `latest` (resolveLatestVersion) and uses
   * this tested version when npm cannot be reached.
   */
  version: string;
  variables: IConnectionVariable[];
  snippet: string;
}

export const CONNECTION_PRESETS: IConnectionPreset[] = [
  {
    id: "blocks",
    label: "Blocks APIs",
    description:
      "Data, mail, IAM and notifier through @seliseblocks/client, as the signed-in caller — or a client-credentials client when there is none.",
    packageName: "@seliseblocks/client",
    version: "0.2.0",
    variables: [
      {
        key: "BLOCKS_API_URL",
        secret: false,
        hint: "The project's API host: https://blocksapi.<your domain>, or the shared public host until the project has a custom domain",
        prefill: "blocksApiHost",
      },
      {
        key: "BLOCKS_CLIENT_ID",
        secret: true,
        hint: "Optional: an IAM client-credentials client, used only when a run has no caller token (public trigger, schedule)",
      },
      {
        key: "BLOCKS_CLIENT_SECRET",
        secret: true,
        hint: "Optional: that client's secret",
      },
    ],
    snippet: `import { createBlocksClient } from "@seliseblocks/client";

export default async function (input, ctx) {
  const base = { apiUrl: ctx.env.BLOCKS_API_URL, xBlocksKey: ctx.context.tenantId };

  // The caller's own token: Blocks applies their roles and permissions. Fetched only now, when
  // this path needs it (one IAM round trip per call). undefined on a public trigger, a schedule,
  // or a client-credentials or impersonated caller.
  let accessToken = await ctx.blocks.getAccessToken();

  if (!accessToken) {
    // No caller token: act as a client-credentials client instead. That token is the client's,
    // not a user's — never hand its rights to an anonymous HTTP caller.
    if (ctx.run.invokedBy.type === "http" && !ctx.context.isAuthenticated) {
      throw new Error("sign-in required");
    }
    if (!ctx.env.BLOCKS_CLIENT_ID || !ctx.env.BLOCKS_CLIENT_SECRET) {
      throw new Error("No caller token and no BLOCKS_CLIENT_ID / BLOCKS_CLIENT_SECRET set");
    }
    const login = await createBlocksClient(base).auth.oidc.clientCredentials({
      clientId: ctx.env.BLOCKS_CLIENT_ID,
      clientSecret: ctx.env.BLOCKS_CLIENT_SECRET,
    });
    if (!login?.access_token) throw new Error("Blocks client-credentials login failed");
    accessToken = login.access_token;
  }

  const blocks = createBlocksClient({ ...base, accessToken });
  return await blocks.data.collection("Students").list();
}`,
  },
  {
    id: "mongodb",
    label: "MongoDB",
    description: "Any MongoDB with a public endpoint — Atlas, Cosmos DB for MongoDB.",
    packageName: "mongodb",
    version: "7.7.0",
    variables: [
      {
        key: "MONGO_URL",
        secret: true,
        hint: "mongodb+srv://user:password@cluster.example.net/db",
      },
    ],
    snippet: `import { MongoClient } from "mongodb";

export default async function (input, ctx) {
  const mongo = new MongoClient(ctx.env.MONGO_URL, { serverSelectionTimeoutMS: 5000 });
  try {
    await mongo.connect();
    await mongo.db().collection("events").insertOne({ ...input, at: new Date() });
    return { ok: true };
  } finally {
    await mongo.close();
  }
}`,
  },
  {
    id: "postgres",
    label: "PostgreSQL",
    description: "Any Postgres with a public endpoint — Neon, Supabase, Azure, RDS.",
    packageName: "pg",
    version: "8.23.1",
    variables: [
      {
        key: "PG_URL",
        secret: true,
        hint: "postgres://user:password@host:5432/db?sslmode=require",
      },
    ],
    snippet: `import pg from "pg";

export default async function (input, ctx) {
  const db = new pg.Client({ connectionString: ctx.env.PG_URL, connectionTimeoutMillis: 5000 });
  try {
    await db.connect();
    const { rows } = await db.query("select now() as now");
    return rows[0];
  } finally {
    await db.end();
  }
}`,
  },
  {
    id: "mysql",
    label: "MySQL",
    description: "Any MySQL or MariaDB with a public endpoint — PlanetScale, Azure, RDS.",
    packageName: "mysql2",
    version: "3.24.5",
    variables: [{ key: "MYSQL_URL", secret: true, hint: "mysql://user:password@host:3306/db" }],
    snippet: `import mysql from "mysql2/promise";

export default async function (input, ctx) {
  const db = await mysql.createConnection({ uri: ctx.env.MYSQL_URL, connectTimeout: 5000 });
  try {
    const [rows] = await db.query("select now() as now");
    return rows[0];
  } finally {
    await db.end();
  }
}`,
  },
  {
    id: "redis",
    label: "Redis",
    description: "Any Redis with a public TLS endpoint — Upstash, Redis Cloud, Azure Cache.",
    packageName: "ioredis",
    version: "6.0.0",
    variables: [{ key: "REDIS_URL", secret: true, hint: "rediss://default:password@host:6380" }],
    snippet: `import Redis from "ioredis";

export default async function (input, ctx) {
  const redis = new Redis(ctx.env.REDIS_URL, {
    lazyConnect: true,
    connectTimeout: 5000,
    maxRetriesPerRequest: 1,
  });
  try {
    await redis.connect();
    return { count: await redis.incr("events:count") };
  } finally {
    redis.disconnect();
  }
}`,
  },
  {
    id: "rabbitmq",
    label: "RabbitMQ",
    description: "Publish to any AMQP broker with a public endpoint — CloudAMQP, Amazon MQ.",
    packageName: "amqplib",
    version: "2.2.0",
    variables: [
      { key: "AMQP_URL", secret: true, hint: "amqps://user:password@host/vhost" },
      { key: "AMQP_QUEUE", secret: false, hint: "Queue to publish to", defaultValue: "events" },
    ],
    snippet: `import amqp from "amqplib";

export default async function (input, ctx) {
  const conn = await amqp.connect(ctx.env.AMQP_URL);
  try {
    const channel = await conn.createConfirmChannel();
    await channel.assertQueue(ctx.env.AMQP_QUEUE, { durable: true });
    channel.sendToQueue(ctx.env.AMQP_QUEUE, Buffer.from(JSON.stringify(input)), { persistent: true });
    // Return only once the broker has the message.
    await channel.waitForConfirms();
    return { ok: true };
  } finally {
    await conn.close();
  }
}`,
  },
  {
    id: "servicebus",
    label: "Azure Service Bus",
    description: "Send to your own Service Bus namespace.",
    packageName: "@azure/service-bus",
    version: "7.10.0",
    variables: [
      {
        key: "SERVICEBUS_CONNECTION",
        secret: true,
        hint: "Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=…;SharedAccessKey=…",
      },
      { key: "SERVICEBUS_QUEUE", secret: false, hint: "Queue or topic name" },
    ],
    snippet: `import { ServiceBusClient } from "@azure/service-bus";

export default async function (input, ctx) {
  const client = new ServiceBusClient(ctx.env.SERVICEBUS_CONNECTION);
  try {
    const sender = client.createSender(ctx.env.SERVICEBUS_QUEUE);
    await sender.sendMessages({ body: input });
    await sender.close();
    return { ok: true };
  } finally {
    await client.close();
  }
}`,
  },
  {
    id: "sqs",
    label: "Amazon SQS",
    description: "Send to an SQS queue with an IAM user's access key.",
    packageName: "@aws-sdk/client-sqs",
    version: "3.1146.0",
    variables: [
      { key: "AWS_REGION", secret: false, hint: "The queue's region, e.g. eu-west-1" },
      {
        key: "SQS_QUEUE_URL",
        secret: true,
        hint: "https://sqs.<region>.amazonaws.com/<account id>/<queue name>",
      },
      {
        key: "AWS_ACCESS_KEY_ID",
        secret: true,
        hint: "Access key of an IAM user allowed sqs:SendMessage on that queue",
      },
      { key: "AWS_SECRET_ACCESS_KEY", secret: true, hint: "That access key's secret" },
    ],
    snippet: `import { SQSClient, SendMessageCommand } from "@aws-sdk/client-sqs";

export default async function (input, ctx) {
  const sqs = new SQSClient({
    region: ctx.env.AWS_REGION,
    credentials: {
      accessKeyId: ctx.env.AWS_ACCESS_KEY_ID,
      secretAccessKey: ctx.env.AWS_SECRET_ACCESS_KEY,
    },
    maxAttempts: 2,
    requestHandler: { connectionTimeout: 5000, requestTimeout: 10000 },
  });
  try {
    // A FIFO queue (name ends in .fifo) also needs MessageGroupId.
    const sent = await sqs.send(
      new SendMessageCommand({
        QueueUrl: ctx.env.SQS_QUEUE_URL,
        MessageBody: JSON.stringify(input),
      }),
    );
    return { ok: true, messageId: sent.MessageId };
  } finally {
    sqs.destroy();
  }
}`,
  },
];
