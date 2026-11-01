import { tStatic } from "@seliseblocks/genesis-os";
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
    get label() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.BLOCKS_APIS");
    },
    get description() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.DATA_MAIL_IAM_AND");
    },
    packageName: "@seliseblocks/client",
    version: "0.2.0",
    variables: [
      {
        key: "BLOCKS_API_URL",
        secret: false,
        get hint() {
          return tStatic("logic.functions", "CONNECTIONS_CONSTANT.THE_PROJECT_S_API");
        },
        prefill: "blocksApiHost",
      },
      {
        key: "BLOCKS_CLIENT_ID",
        secret: true,
        get hint() {
          return tStatic("logic.functions", "CONNECTIONS_CONSTANT.OPTIONAL_AN_IAM_CLIENT");
        },
      },
      {
        key: "BLOCKS_CLIENT_SECRET",
        secret: true,
        get hint() {
          return tStatic("logic.functions", "CONNECTIONS_CONSTANT.OPTIONAL_THAT_CLIENT_S");
        },
      },
    ],
    snippet: `import { createBlocksClient } from "@seliseblocks/client";

export default async function (input, ctx) {
  const base = { apiUrl: ctx.env.BLOCKS_API_URL, xBlocksKey: ctx.context.tenantId };

  // The caller's own token: Blocks applies their roles and permissions. Fetched only now, when
  // this path needs it (one IAM round trip per call). undefined when there is no signed-in
  // caller (a public trigger, a scheduled workflow) or for a client-credentials caller.
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
    get description() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.ANY_MONGODB_WITH_A");
    },
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
    get description() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.ANY_POSTGRES_WITH_A");
    },
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
    get description() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.ANY_MYSQL_OR_MARIADB");
    },
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
    get label() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.REDIS");
    },
    get description() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.ANY_REDIS_WITH_A");
    },
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
    get description() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.PUBLISH_TO_ANY_AMQP");
    },
    packageName: "amqplib",
    version: "2.2.0",
    variables: [
      { key: "AMQP_URL", secret: true, hint: "amqps://user:password@host/vhost" },
      {
        key: "AMQP_QUEUE",
        secret: false,
        get hint() {
          return tStatic("logic.functions", "CONNECTIONS_CONSTANT.QUEUE_TO_PUBLISH_TO");
        },
        defaultValue: "events",
      },
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
    get label() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.AZURE_SERVICE_BUS");
    },
    get description() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.SEND_TO_YOUR_OWN");
    },
    packageName: "@azure/service-bus",
    version: "7.10.0",
    variables: [
      {
        key: "SERVICEBUS_CONNECTION",
        secret: true,
        get hint() {
          return tStatic(
            "logic.functions",
            "CONNECTIONS_CONSTANT.ENDPOINT_SB_NAMESPACE_SERVICEBUS",
          );
        },
      },
      {
        key: "SERVICEBUS_QUEUE",
        secret: false,
        get hint() {
          return tStatic("logic.functions", "CONNECTIONS_CONSTANT.QUEUE_OR_TOPIC_NAME");
        },
      },
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
    get label() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.AMAZON_SQS");
    },
    get description() {
      return tStatic("logic.functions", "CONNECTIONS_CONSTANT.SEND_TO_AN_SQS");
    },
    packageName: "@aws-sdk/client-sqs",
    version: "3.1146.0",
    variables: [
      {
        key: "AWS_REGION",
        secret: false,
        get hint() {
          return tStatic("logic.functions", "CONNECTIONS_CONSTANT.THE_QUEUE_S_REGION");
        },
      },
      {
        key: "SQS_QUEUE_URL",
        secret: true,
        hint: "https://sqs.<region>.amazonaws.com/<account id>/<queue name>",
      },
      {
        key: "AWS_ACCESS_KEY_ID",
        secret: true,
        get hint() {
          return tStatic("logic.functions", "CONNECTIONS_CONSTANT.ACCESS_KEY_OF_AN");
        },
      },
      {
        key: "AWS_SECRET_ACCESS_KEY",
        secret: true,
        get hint() {
          return tStatic("logic.functions", "CONNECTIONS_CONSTANT.THAT_ACCESS_KEY_S");
        },
      },
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
